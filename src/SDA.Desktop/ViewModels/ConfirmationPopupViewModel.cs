using SDA.Desktop.Services;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.ViewModels
{
    public sealed class PendingPopupConfirmation
    {
        public PendingPopupConfirmation(SteamGuardAccount account, Confirmation confirmation)
        {
            Account = account;
            Confirmation = confirmation;
        }

        public SteamGuardAccount Account { get; }

        public Confirmation Confirmation { get; }

        public string ConfirmationKey
        {
            get { return ConfirmationIdentity.Key(Account, Confirmation); }
        }
    }

    public static class ConfirmationIdentity
    {
        public static string Key(SteamGuardAccount account, Confirmation confirmation)
        {
            ulong steamId = account == null || account.Session == null ? 0 : account.Session.SteamID;
            string accountName = account == null ? "" : account.AccountName ?? "";
            ulong id = confirmation == null ? 0 : confirmation.ID;
            ulong nonce = confirmation == null ? 0 : confirmation.Key;
            return steamId.ToString() + "|" + accountName + "|" + id.ToString() + "|" + nonce.ToString();
        }
    }

    public sealed class ConfirmationPopupViewModel : INotifyPropertyChanged
    {
        public const string AcceptAgainText = "Press Accept again to confirm";
        public const string DenyAgainText = "Press Deny again to confirm";
        public const string AcceptFailedText = "Unable to accept confirmation.";
        public const string DenyFailedText = "Unable to deny confirmation.";

        private readonly IConfirmationClient _client;
        private readonly Queue<PendingPopupConfirmation> _queue = new Queue<PendingPopupConfirmation>();
        private readonly HashSet<string> _queuedKeys = new HashSet<string>(StringComparer.Ordinal);
        private PendingPopupConfirmation _current;
        private bool _acceptArmed;
        private bool _denyArmed;
        private bool _busy;
        private string _statusText = "";

        public ConfirmationPopupViewModel(IConfirmationClient client)
        {
            _client = client ?? throw new ArgumentNullException("client");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler VisibilityChanged;

        public PendingPopupConfirmation Current
        {
            get { return _current; }
        }

        public SteamGuardAccount CurrentAccount
        {
            get { return _current == null ? null : _current.Account; }
        }

        public Confirmation CurrentConfirmation
        {
            get { return _current == null ? null : _current.Confirmation; }
        }

        public bool IsVisible
        {
            get { return _current != null; }
        }

        public int QueueCount
        {
            get { return _queue.Count + (_current == null ? 0 : 1); }
        }

        public bool AcceptArmed
        {
            get { return _acceptArmed; }
        }

        public bool DenyArmed
        {
            get { return _denyArmed; }
        }

        public bool CanAct
        {
            get { return _current != null && !_busy; }
        }

        public string AccountName
        {
            get
            {
                if (_current == null || _current.Account == null)
                {
                    return "";
                }

                return _current.Account.AccountName ?? "";
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set
            {
                _statusText = value ?? "";
                OnPropertyChanged();
            }
        }

        public bool Enqueue(SteamGuardAccount account, Confirmation confirmation)
        {
            if (account == null || confirmation == null)
            {
                return false;
            }

            string key = ConfirmationIdentity.Key(account, confirmation);
            if (_queuedKeys.Contains(key))
            {
                return false;
            }

            _queuedKeys.Add(key);
            PendingPopupConfirmation pending = new PendingPopupConfirmation(account, confirmation);
            if (_current == null)
            {
                SetCurrent(pending);
            }
            else
            {
                _queue.Enqueue(pending);
            }

            return true;
        }

        public int EnqueueMany(IEnumerable<PendingPopupConfirmation> items)
        {
            int added = 0;
            if (items == null)
            {
                return added;
            }

            foreach (PendingPopupConfirmation item in items)
            {
                if (item != null && Enqueue(item.Account, item.Confirmation))
                {
                    added++;
                }
            }

            return added;
        }

        public Task AcceptAsync()
        {
            return AcceptAsync(CancellationToken.None);
        }

        public async Task AcceptAsync(CancellationToken cancellationToken)
        {
            if (_current == null || _busy)
            {
                return;
            }

            if (!_acceptArmed)
            {
                _acceptArmed = true;
                _denyArmed = false;
                StatusText = AcceptAgainText;
                OnPropertyChanged(nameof(AcceptArmed));
                OnPropertyChanged(nameof(DenyArmed));
                return;
            }

            await ExecuteAsync(true, cancellationToken);
        }

        public Task DenyAsync()
        {
            return DenyAsync(CancellationToken.None);
        }

        public async Task DenyAsync(CancellationToken cancellationToken)
        {
            if (_current == null || _busy)
            {
                return;
            }

            if (!_denyArmed)
            {
                _denyArmed = true;
                _acceptArmed = false;
                StatusText = DenyAgainText;
                OnPropertyChanged(nameof(AcceptArmed));
                OnPropertyChanged(nameof(DenyArmed));
                return;
            }

            await ExecuteAsync(false, cancellationToken);
        }

        private async Task ExecuteAsync(bool accept, CancellationToken cancellationToken)
        {
            _busy = true;
            OnPropertyChanged(nameof(CanAct));
            try
            {
                bool succeeded = accept
                    ? await _client.AcceptAsync(_current.Account, _current.Confirmation, cancellationToken)
                    : await _client.DenyAsync(_current.Account, _current.Confirmation, cancellationToken);
                if (!succeeded)
                {
                    ResetArmed();
                    StatusText = accept ? AcceptFailedText : DenyFailedText;
                    return;
                }

                AdvanceAfterSuccess();
            }
            catch (OperationCanceledException)
            {
                ResetArmed();
                StatusText = accept ? AcceptFailedText : DenyFailedText;
            }
            catch (Exception)
            {
                ResetArmed();
                StatusText = accept ? AcceptFailedText : DenyFailedText;
            }
            finally
            {
                _busy = false;
                OnPropertyChanged(nameof(CanAct));
            }
        }

        private void AdvanceAfterSuccess()
        {
            if (_current != null)
            {
                _queuedKeys.Remove(_current.ConfirmationKey);
            }

            PendingPopupConfirmation next = _queue.Count > 0 ? _queue.Dequeue() : null;
            SetCurrent(next);
        }

        private void SetCurrent(PendingPopupConfirmation current)
        {
            bool wasVisible = IsVisible;
            _current = current;
            ResetArmed();
            StatusText = "";
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(CurrentAccount));
            OnPropertyChanged(nameof(CurrentConfirmation));
            OnPropertyChanged(nameof(AccountName));
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(QueueCount));
            OnPropertyChanged(nameof(CanAct));
            if (wasVisible != IsVisible)
            {
                EventHandler handler = VisibilityChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        private void ResetArmed()
        {
            _acceptArmed = false;
            _denyArmed = false;
            OnPropertyChanged(nameof(AcceptArmed));
            OnPropertyChanged(nameof(DenyArmed));
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
