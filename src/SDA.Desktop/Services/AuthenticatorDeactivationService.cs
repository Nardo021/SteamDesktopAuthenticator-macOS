using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public enum DeactivationStatus
    {
        Succeeded,
        SessionExpired,
        RefreshFailed,
        ConfirmationMismatch,
        RemoteFailed,
        LocalCleanupFailed,
        Cancelled,
        Busy
    }

    public sealed class DeactivationResult
    {
        public DeactivationResult(DeactivationStatus status, string message, int scheme, bool remoteSucceeded)
        {
            Status = status;
            Message = message ?? "";
            Scheme = scheme;
            RemoteSucceeded = remoteSucceeded;
        }

        public DeactivationStatus Status { get; }

        public string Message { get; }

        public int Scheme { get; }

        public bool RemoteSucceeded { get; }
    }

    public interface IAuthenticatorDeactivator
    {
        Task<bool> DeactivateAsync(SteamGuardAccount account, int scheme, CancellationToken cancellationToken);
    }

    public sealed class SteamAuthAuthenticatorDeactivator : IAuthenticatorDeactivator
    {
        public Task<bool> DeactivateAsync(SteamGuardAccount account, int scheme, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return account.DeactivateAuthenticator(scheme);
        }
    }

    public interface ISteamGuardCodeGenerator
    {
        string Generate(SteamGuardAccount account);
    }

    public sealed class SteamAuthGuardCodeGenerator : ISteamGuardCodeGenerator
    {
        public string Generate(SteamGuardAccount account)
        {
            return account.GenerateSteamGuardCode();
        }
    }

    public class AuthenticatorDeactivationService
    {
        public const int EmailScheme = 1;
        public const int RemoveCompletelyScheme = 2;

        public const string SessionExpiredMessage = "Your session has expired. Use Login Again under Selected Account.";
        public const string ConfirmationMismatchMessage = "Confirmation codes do not match. Steam Guard not removed.";
        public const string RemoteFailedMessage = "Steam Guard failed to deactivate.";
        public const string LocalCleanupFailedMessage = "Steam authenticator deactivation succeeded, but SDA could not fully remove the local maFile.";
        public const string CancelledMessage = "Steam Guard was not removed. No action was taken.";

        private readonly IAuthenticatorDeactivator _deactivator;
        private readonly IAccessTokenRefresher _refresher;
        private readonly SessionPersistenceService _persistence;
        private int _busy;

        public AuthenticatorDeactivationService()
            : this(new SteamAuthAuthenticatorDeactivator(), new SteamAccessTokenRefresher(), new SessionPersistenceService())
        {
        }

        public AuthenticatorDeactivationService(
            IAuthenticatorDeactivator deactivator,
            IAccessTokenRefresher refresher,
            SessionPersistenceService persistence)
        {
            _deactivator = deactivator ?? new SteamAuthAuthenticatorDeactivator();
            _refresher = refresher ?? new SteamAccessTokenRefresher();
            _persistence = persistence ?? new SessionPersistenceService();
        }

        public int RemoteCalls { get; private set; }

        public SessionTokenState InspectSession(SteamGuardAccount account)
        {
            return SessionStateInspector.Inspect(account == null ? null : account.Session);
        }

        public async Task<DeactivationResult> PrepareSessionAsync(
            SteamGuardAccount account,
            string directory,
            string passKey,
            CancellationToken cancellationToken)
        {
            SessionTokenState state = InspectSession(account);
            if (state == SessionTokenState.Missing || state == SessionTokenState.RefreshTokenExpired)
            {
                return new DeactivationResult(DeactivationStatus.SessionExpired, SessionExpiredMessage, 0, false);
            }

            if (state == SessionTokenState.AccessTokenExpired)
            {
                try
                {
                    await _refresher.RefreshAsync(account.Session, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new DeactivationResult(DeactivationStatus.Cancelled, CancelledMessage, 0, false);
                }
                catch (Exception)
                {
                    return new DeactivationResult(DeactivationStatus.RefreshFailed, SessionExpiredMessage, 0, false);
                }

                if (!string.IsNullOrEmpty(directory))
                {
                    _persistence.Commit(account, account.Session, directory, passKey, false);
                }
            }

            return new DeactivationResult(DeactivationStatus.Succeeded, "", 0, false);
        }

        public bool ConfirmationMatches(string expected, string entered)
        {
            if (string.IsNullOrEmpty(expected))
            {
                return false;
            }

            return string.Equals(entered == null ? "" : entered.ToUpperInvariant(), expected.ToUpperInvariant(), StringComparison.Ordinal);
        }

        public static string SuccessAcknowledgement(int scheme)
        {
            if (scheme == RemoveCompletelyScheme)
            {
                return "Steam Guard was removed completely. The local maFile will be deleted when you continue. If you need a backup, make it now.";
            }

            return "Steam Guard was switched to email authentication. The local maFile will be deleted when you continue. If you need a backup, make it now.";
        }

        public async Task<DeactivationResult> DeactivateRemoteAsync(
            SteamGuardAccount account,
            int scheme,
            CancellationToken cancellationToken)
        {
            if (scheme != EmailScheme && scheme != RemoveCompletelyScheme)
            {
                return new DeactivationResult(DeactivationStatus.Cancelled, CancelledMessage, scheme, false);
            }

            if (Interlocked.Exchange(ref _busy, 1) == 1)
            {
                return new DeactivationResult(DeactivationStatus.Busy, RemoteFailedMessage, scheme, false);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemoteCalls++;
                bool remote = await _deactivator.DeactivateAsync(account, scheme, cancellationToken).ConfigureAwait(false);
                if (!remote)
                {
                    return new DeactivationResult(DeactivationStatus.RemoteFailed, RemoteFailedMessage, scheme, false);
                }

                return new DeactivationResult(DeactivationStatus.Succeeded, SuccessAcknowledgement(scheme), scheme, true);
            }
            catch (OperationCanceledException)
            {
                return new DeactivationResult(DeactivationStatus.Cancelled, CancelledMessage, scheme, false);
            }
            catch (Exception)
            {
                return new DeactivationResult(DeactivationStatus.RemoteFailed, RemoteFailedMessage, scheme, false);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        public DeactivationResult CleanupLocalAfterRemoteSuccess(SteamGuardAccount account, string directory, int scheme)
        {
            DeactivationStatus local = ManifestMutationGate.Shared.Run(() => RemoveLocalAfterRemoteSuccess(account, directory));
            if (local == DeactivationStatus.LocalCleanupFailed)
            {
                return new DeactivationResult(DeactivationStatus.LocalCleanupFailed, LocalCleanupFailedMessage, scheme, true);
            }

            return new DeactivationResult(DeactivationStatus.Succeeded, SuccessAcknowledgement(scheme), scheme, true);
        }

        private static DeactivationStatus RemoveLocalAfterRemoteSuccess(SteamGuardAccount account, string directory)
        {
            if (account == null || string.IsNullOrEmpty(directory))
            {
                return DeactivationStatus.LocalCleanupFailed;
            }

            Manifest manifest;
            try
            {
                manifest = Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return DeactivationStatus.LocalCleanupFailed;
            }

            try
            {
                manifest.RemoveAccount(account, true);
            }
            catch (Exception)
            {
                return DeactivationStatus.LocalCleanupFailed;
            }

            Manifest reloaded;
            try
            {
                reloaded = Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return DeactivationStatus.LocalCleanupFailed;
            }

            if (EntryExists(reloaded, account.Session == null ? 0 : account.Session.SteamID))
            {
                return DeactivationStatus.LocalCleanupFailed;
            }

            string maFile = account.Session == null ? null : Path.Combine(directory, account.Session.SteamID + ".maFile");
            if (!string.IsNullOrEmpty(maFile) && File.Exists(maFile))
            {
                return DeactivationStatus.LocalCleanupFailed;
            }

            return DeactivationStatus.Succeeded;
        }

        private static bool EntryExists(Manifest manifest, ulong steamId)
        {
            if (steamId == 0 || manifest == null || manifest.Entries == null)
            {
                return false;
            }

            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                if (manifest.Entries[i].SteamID == steamId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
