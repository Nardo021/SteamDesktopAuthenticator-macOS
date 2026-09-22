using SteamAuth;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IConfirmationClient
    {
        Task<Confirmation[]> FetchAsync(SteamGuardAccount account, CancellationToken cancellationToken);

        Task<bool> AcceptAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken);

        Task<bool> DenyAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken);

        Task<bool> AcceptMultipleAsync(SteamGuardAccount account, Confirmation[] confirmations, CancellationToken cancellationToken);
    }

    public sealed class SteamGuardAccountConfirmationClient : IConfirmationClient
    {
        public Task<Confirmation[]> FetchAsync(SteamGuardAccount account, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return account.FetchConfirmationsAsync();
        }

        public Task<bool> AcceptAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return account.AcceptConfirmation(confirmation);
        }

        public Task<bool> DenyAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return account.DenyConfirmation(confirmation);
        }

        public Task<bool> AcceptMultipleAsync(SteamGuardAccount account, Confirmation[] confirmations, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return account.AcceptMultipleConfirmations(confirmations);
        }
    }

    public enum ConfirmationLoadStatus
    {
        Loaded,
        Empty,
        SessionExpired,
        Failed
    }

    public sealed class ConfirmationLoadResult
    {
        private ConfirmationLoadResult(ConfirmationLoadStatus status, Confirmation[] confirmations, string message, bool sessionWasRefreshed)
        {
            Status = status;
            Confirmations = confirmations ?? new Confirmation[0];
            Message = message ?? "";
            SessionWasRefreshed = sessionWasRefreshed;
        }

        public ConfirmationLoadStatus Status { get; }

        public Confirmation[] Confirmations { get; }

        public string Message { get; }

        public bool SessionWasRefreshed { get; }

        public static ConfirmationLoadResult Loaded(Confirmation[] confirmations, bool sessionWasRefreshed)
        {
            return new ConfirmationLoadResult(ConfirmationLoadStatus.Loaded, confirmations, "", sessionWasRefreshed);
        }

        public static ConfirmationLoadResult Empty(bool sessionWasRefreshed)
        {
            return new ConfirmationLoadResult(ConfirmationLoadStatus.Empty, new Confirmation[0], "Nothing to confirm/cancel", sessionWasRefreshed);
        }

        public static ConfirmationLoadResult SessionExpired()
        {
            return new ConfirmationLoadResult(
                ConfirmationLoadStatus.SessionExpired,
                new Confirmation[0],
                "Your session has expired. Use Login Again under Selected Account.",
                false);
        }

        public static ConfirmationLoadResult Failed(string message)
        {
            return new ConfirmationLoadResult(
                ConfirmationLoadStatus.Failed,
                new Confirmation[0],
                string.IsNullOrEmpty(message) ? "Unable to load confirmations." : message,
                false);
        }
    }

    public sealed class ConfirmationService
    {
        public const string SessionExpiredMessage = "Your session has expired. Use Login Again under Selected Account.";
        public const string ForceRefreshMessage = "Steam needs a refreshed session. Use Force Session Refresh under Selected Account.";
        public const string EmptyMessage = "Nothing to confirm/cancel";

        private readonly IConfirmationClient _client;
        private readonly SessionRefreshService _refresh;

        public ConfirmationService(IConfirmationClient client, SessionRefreshService refresh = null)
        {
            _client = client ?? new SteamGuardAccountConfirmationClient();
            _refresh = refresh ?? new SessionRefreshService(null);
        }

        public IConfirmationClient Client
        {
            get { return _client; }
        }

        public async Task<ConfirmationLoadResult> LoadAsync(
            SteamGuardAccount account,
            Func<SteamGuardAccount, SessionData, Task<string>> persist,
            CancellationToken cancellationToken)
        {
            if (account == null)
            {
                return ConfirmationLoadResult.Failed("Unable to load confirmations.");
            }

            SessionTokenState state = SessionStateInspector.Inspect(account.Session);
            if (state == SessionTokenState.Missing || state == SessionTokenState.RefreshTokenExpired)
            {
                return ConfirmationLoadResult.SessionExpired();
            }

            bool refreshed = false;
            if (state == SessionTokenState.AccessTokenExpired)
            {
                SessionData previous = SessionPersistenceService.Clone(account.Session);
                try
                {
                    await _refresh.RefreshAsync(account.Session, cancellationToken);
                    refreshed = true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    account.Session = previous;
                    return ConfirmationLoadResult.Failed("Unable to refresh session.");
                }

                if (persist == null)
                {
                    account.Session = previous;
                    return ConfirmationLoadResult.Failed("Unable to save refreshed session.");
                }

                string saveError;
                try
                {
                    saveError = await persist(account, account.Session);
                }
                catch (OperationCanceledException)
                {
                    account.Session = previous;
                    throw;
                }
                catch (Exception)
                {
                    account.Session = previous;
                    return ConfirmationLoadResult.Failed("Unable to save refreshed session.");
                }

                if (!string.IsNullOrEmpty(saveError))
                {
                    account.Session = previous;
                    return ConfirmationLoadResult.Failed(saveError);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Confirmation[] confirmations = await _client.FetchAsync(account, cancellationToken);
                if (confirmations == null || confirmations.Length == 0)
                {
                    return ConfirmationLoadResult.Empty(refreshed);
                }

                return ConfirmationLoadResult.Loaded(confirmations, refreshed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return MapFetchError(account, ex);
            }
        }

        private static ConfirmationLoadResult MapFetchError(SteamGuardAccount account, Exception ex)
        {
            if (ex is ArgumentException && string.Equals(ex.Message, "Device ID is not present", StringComparison.Ordinal))
            {
                return ConfirmationLoadResult.Failed("Account is missing a device ID.");
            }

            if (IsNeedsAuthentication(ex))
            {
                if (SessionStateInspector.CanForceRefresh(account == null ? null : account.Session))
                {
                    return ConfirmationLoadResult.Failed(ForceRefreshMessage);
                }

                return ConfirmationLoadResult.SessionExpired();
            }

            return ConfirmationLoadResult.Failed("Unable to load confirmations.");
        }

        private static bool IsNeedsAuthentication(Exception ex)
        {
            return ex != null && string.Equals(ex.Message, "Needs Authentication", StringComparison.Ordinal);
        }
    }
}
