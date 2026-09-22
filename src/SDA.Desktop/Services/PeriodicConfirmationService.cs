using SDA.Desktop.ViewModels;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public sealed class PeriodicPollSnapshot
    {
        public PeriodicPollSnapshot(IReadOnlyList<SteamGuardAccount> accounts, string directory, string passKey)
        {
            Accounts = accounts ?? Array.Empty<SteamGuardAccount>();
            Directory = directory;
            PassKey = passKey;
        }

        public IReadOnlyList<SteamGuardAccount> Accounts { get; }

        public string Directory { get; }

        public string PassKey { get; }
    }

    public sealed class PeriodicPollResult
    {
        public int AccountsInspected { get; set; }

        public int Fetches { get; set; }

        public int AutoAccepted { get; set; }

        public int PopupQueued { get; set; }

        public int SkippedExpiredRefresh { get; set; }

        public bool OverlapSkipped { get; set; }
    }

    public sealed class PeriodicConfirmationService
    {
        public const string SessionExpiredStatusPrefix = "Your session for ";
        public const string SessionExpiredStatusSuffix = " has expired. Use Login Again.";
        public const string AutoConfirmFailedStatus = "Unable to auto-confirm some confirmations.";

        private readonly ConfirmationService _confirmations;
        private readonly IConfirmationClient _client;
        private readonly Func<PeriodicPollSnapshot> _snapshot;
        private readonly Func<SteamGuardAccount, SessionData, Task<string>> _persist;
        private readonly ConfirmationPopupViewModel _popup;
        private readonly HashSet<string> _sessionExpiredAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();
        private ManifestRuntimeSettings _settings = new ManifestRuntimeSettings();
        private int _pollInFlight;

        public PeriodicConfirmationService(
            ConfirmationService confirmations,
            IConfirmationClient client,
            Func<PeriodicPollSnapshot> snapshot,
            Func<SteamGuardAccount, SessionData, Task<string>> persist,
            ConfirmationPopupViewModel popup)
        {
            _confirmations = confirmations ?? throw new ArgumentNullException("confirmations");
            _client = client ?? throw new ArgumentNullException("client");
            _snapshot = snapshot ?? throw new ArgumentNullException("snapshot");
            _persist = persist;
            _popup = popup ?? throw new ArgumentNullException("popup");
        }

        public event Action<string> StatusRaised;

        public ManifestRuntimeSettings Settings
        {
            get { return _settings.Clone(); }
        }

        public int EffectiveIntervalSeconds
        {
            get { return _settings.EffectiveIntervalSeconds; }
        }

        public int PollAttempts { get; private set; }

        public int CompletedPolls { get; private set; }

        public int OverlappedTicksSkipped { get; private set; }

        public void ApplySettings(ManifestRuntimeSettings settings)
        {
            _settings = settings == null ? new ManifestRuntimeSettings() : settings.Clone();
        }

        public void ClearSessionExpiredSuppression()
        {
            lock (_gate)
            {
                _sessionExpiredAccounts.Clear();
            }
        }

        public void ForgetSessionExpired(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
            {
                return;
            }

            lock (_gate)
            {
                _sessionExpiredAccounts.Remove(accountName);
            }
        }

        public Task<PeriodicPollResult> PollAsync()
        {
            return PollAsync(CancellationToken.None);
        }

        public async Task<PeriodicPollResult> PollAsync(CancellationToken cancellationToken)
        {
            PeriodicPollResult result = new PeriodicPollResult();
            PollAttempts++;
            if (Interlocked.CompareExchange(ref _pollInFlight, 1, 0) != 0)
            {
                OverlappedTicksSkipped++;
                result.OverlapSkipped = true;
                return result;
            }

            try
            {
                if (!_settings.PeriodicChecking)
                {
                    return result;
                }

                PeriodicPollSnapshot snapshot = _snapshot();
                if (snapshot == null || snapshot.Accounts == null || snapshot.Accounts.Count == 0)
                {
                    return result;
                }

                foreach (SteamGuardAccount account in snapshot.Accounts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result.AccountsInspected++;
                    await PollAccountAsync(account, result, cancellationToken);
                }

                CompletedPolls++;
                return result;
            }
            catch (OperationCanceledException)
            {
                return result;
            }
            finally
            {
                Interlocked.Exchange(ref _pollInFlight, 0);
            }
        }

        private async Task PollAccountAsync(
            SteamGuardAccount account,
            PeriodicPollResult result,
            CancellationToken cancellationToken)
        {
            if (account == null)
            {
                return;
            }

            SessionTokenState state = SessionStateInspector.Inspect(account.Session);
            if (state == SessionTokenState.Missing || state == SessionTokenState.RefreshTokenExpired)
            {
                RaiseSessionExpiredOnce(account.AccountName);
                result.SkippedExpiredRefresh++;
                return;
            }

            ConfirmationLoadResult load = await _confirmations.LoadAsync(account, _persist, cancellationToken);
            if (load.Status == ConfirmationLoadStatus.SessionExpired)
            {
                RaiseSessionExpiredOnce(account.AccountName);
                result.SkippedExpiredRefresh++;
                return;
            }

            if (load.Status == ConfirmationLoadStatus.Failed)
            {
                return;
            }

            ForgetSessionExpired(account.AccountName);
            result.Fetches++;

            Confirmation[] confirmations = load.Confirmations ?? Array.Empty<Confirmation>();
            List<Confirmation> autoAccept = new List<Confirmation>();
            List<PendingPopupConfirmation> popup = new List<PendingPopupConfirmation>();
            foreach (Confirmation confirmation in confirmations)
            {
                if (confirmation == null)
                {
                    continue;
                }

                if (confirmation.ConfType == Confirmation.EMobileConfirmationType.MarketListing &&
                    _settings.AutoConfirmMarketTransactions)
                {
                    autoAccept.Add(confirmation);
                    continue;
                }

                if (confirmation.ConfType == Confirmation.EMobileConfirmationType.Trade &&
                    _settings.AutoConfirmTrades)
                {
                    autoAccept.Add(confirmation);
                    continue;
                }

                popup.Add(new PendingPopupConfirmation(account, confirmation));
            }

            if (autoAccept.Count > 0)
            {
                bool accepted;
                try
                {
                    accepted = await _client.AcceptMultipleAsync(account, autoAccept.ToArray(), cancellationToken);
                }
                catch (Exception)
                {
                    accepted = false;
                }

                if (accepted)
                {
                    result.AutoAccepted += autoAccept.Count;
                }
                else
                {
                    RaiseStatus(AutoConfirmFailedStatus);
                }
            }

            if (popup.Count > 0)
            {
                result.PopupQueued += _popup.EnqueueMany(popup);
            }
        }

        private void RaiseSessionExpiredOnce(string accountName)
        {
            string name = accountName ?? "";
            bool first;
            lock (_gate)
            {
                first = _sessionExpiredAccounts.Add(name);
            }

            if (first)
            {
                RaiseStatus(SessionExpiredStatusPrefix + name + SessionExpiredStatusSuffix);
            }
        }

        private void RaiseStatus(string message)
        {
            Action<string> handler = StatusRaised;
            if (handler != null && !string.IsNullOrEmpty(message))
            {
                handler(message);
            }
        }
    }
}
