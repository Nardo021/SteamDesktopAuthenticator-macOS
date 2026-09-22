using SDA.Desktop.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace SDA.Desktop.ViewModels
{
    public sealed class TrayAccountItem
    {
        public TrayAccountItem(AccountViewModel account, bool selected)
        {
            Account = account;
            DisplayName = account == null ? "" : account.DisplayName;
            IsSelected = selected;
        }

        public AccountViewModel Account { get; }

        public string DisplayName { get; }

        public bool IsSelected { get; }
    }

    public interface ITrayMenuActions
    {
        void Restore();

        void SelectAccount(AccountViewModel account);

        Task CopySteamGuardAsync();

        void ViewConfirmations();

        void Quit();
    }

    public sealed class TrayMenuViewModel
    {
        public const string NoAccountsText = "No accounts";

        private readonly ITrayMenuActions _actions;
        private MainWindowViewModel _source;
        private IReadOnlyList<TrayAccountItem> _accounts = Array.Empty<TrayAccountItem>();
        private bool _canCopy;
        private bool _canViewConfirmations;
        private bool _bound;

        public TrayMenuViewModel(ITrayMenuActions actions)
        {
            _actions = actions ?? throw new ArgumentNullException("actions");
        }

        public event EventHandler Changed;

        public IReadOnlyList<TrayAccountItem> Accounts
        {
            get { return _accounts; }
        }

        public bool HasAccounts
        {
            get { return _accounts.Count > 0; }
        }

        public bool CanCopy
        {
            get { return _canCopy; }
        }

        public bool CanViewConfirmations
        {
            get { return _canViewConfirmations; }
        }

        public void Bind(MainWindowViewModel source)
        {
            if (_bound)
            {
                return;
            }

            _source = source;
            if (_source == null)
            {
                Refresh();
                return;
            }

            _source.PropertyChanged += OnSourcePropertyChanged;
            _source.ManifestContextChanged += OnManifestContextChanged;
            _bound = true;
            Refresh();
        }

        public void Unbind()
        {
            if (!_bound || _source == null)
            {
                return;
            }

            _source.PropertyChanged -= OnSourcePropertyChanged;
            _source.ManifestContextChanged -= OnManifestContextChanged;
            _bound = false;
        }

        public void Refresh()
        {
            List<TrayAccountItem> items = new List<TrayAccountItem>();
            if (_source != null && _source.AllAccounts != null)
            {
                foreach (AccountViewModel account in _source.AllAccounts)
                {
                    if (account == null)
                    {
                        continue;
                    }

                    items.Add(new TrayAccountItem(account, SameAccount(account, _source.SelectedAccount)));
                }
            }

            _accounts = items;
            _canViewConfirmations = _source != null && _source.SelectedAccount != null;
            _canCopy = _source != null && _source.CanCopyCode;
            RaiseChanged();
        }

        public void Restore()
        {
            _actions.Restore();
        }

        public void SelectAccount(AccountViewModel account)
        {
            _actions.SelectAccount(account);
        }

        public Task CopySteamGuardAsync()
        {
            if (!_canCopy)
            {
                return Task.CompletedTask;
            }

            return _actions.CopySteamGuardAsync();
        }

        public void ViewConfirmations()
        {
            if (!_canViewConfirmations)
            {
                return;
            }

            _actions.ViewConfirmations();
        }

        public void Quit()
        {
            _actions.Quit();
        }

        private void OnSourcePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null
                || string.IsNullOrEmpty(e.PropertyName)
                || e.PropertyName == nameof(MainWindowViewModel.AllAccounts)
                || e.PropertyName == nameof(MainWindowViewModel.SelectedAccount)
                || e.PropertyName == nameof(MainWindowViewModel.CanCopyCode)
                || e.PropertyName == nameof(MainWindowViewModel.CurrentCode)
                || e.PropertyName == nameof(MainWindowViewModel.AccountCount))
            {
                Refresh();
            }
        }

        private void OnManifestContextChanged(object sender, EventArgs e)
        {
            Refresh();
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static bool SameAccount(AccountViewModel left, AccountViewModel right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left.SteamId != 0 && right.SteamId != 0 && left.SteamId == right.SteamId)
            {
                return true;
            }

            string leftName = left.Account == null ? null : left.Account.AccountName;
            string rightName = right.Account == null ? null : right.Account.AccountName;
            return !string.IsNullOrEmpty(leftName)
                && string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
