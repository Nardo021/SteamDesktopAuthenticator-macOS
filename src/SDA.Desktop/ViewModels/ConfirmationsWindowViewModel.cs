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
    public enum ConfirmationUiState
    {
        Loading,
        Loaded,
        Empty,
        SessionExpired,
        Failed
    }

    public sealed class ConfirmationsWindowViewModel : INotifyPropertyChanged
    {
        private readonly SteamGuardAccount _account;
        private readonly ConfirmationService _loader;
        private readonly IConfirmationClient _actions;
        private readonly IConfirmationIconLoader _icons;
        private readonly Func<SteamGuardAccount, SessionData, Task<string>> _persist;
        private readonly SemaphoreSlim _fetchLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private ConfirmationUiState _state = ConfirmationUiState.Loading;
        private string _statusText = "";
        private IReadOnlyList<ConfirmationViewModel> _confirmations = new ConfirmationViewModel[0];
        private bool _isRefreshing;
        private bool _closed;

        public ConfirmationsWindowViewModel(
            SteamGuardAccount account,
            ConfirmationService loader,
            Func<SteamGuardAccount, SessionData, Task<string>> persist,
            IConfirmationIconLoader icons = null)
        {
            _account = account;
            _loader = loader ?? new ConfirmationService(new SteamGuardAccountConfirmationClient());
            _actions = _loader.Client;
            _persist = persist;
            _icons = icons;
            string name = account == null || string.IsNullOrEmpty(account.AccountName) ? "" : account.AccountName;
            Title = string.IsNullOrEmpty(name) ? "Trade Confirmations" : "Trade Confirmations - " + name;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title { get; }

        public ConfirmationUiState State
        {
            get { return _state; }
            private set
            {
                if (_state == value)
                {
                    return;
                }

                _state = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set
            {
                if (_statusText == value)
                {
                    return;
                }

                _statusText = value ?? "";
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<ConfirmationViewModel> Confirmations
        {
            get { return _confirmations; }
            private set
            {
                _confirmations = value ?? new ConfirmationViewModel[0];
                OnPropertyChanged();
            }
        }

        public bool IsRefreshing
        {
            get { return _isRefreshing; }
            private set
            {
                if (_isRefreshing == value)
                {
                    return;
                }

                _isRefreshing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(RefreshButtonText));
            }
        }

        public bool CanRefresh
        {
            get { return !_isRefreshing && !_closed; }
        }

        public string RefreshButtonText
        {
            get { return _isRefreshing ? "Refreshing..." : "Refresh"; }
        }

        public async Task RefreshAsync()
        {
            if (_closed)
            {
                return;
            }

            try
            {
                if (!await _fetchLock.WaitAsync(0, _lifetime.Token).ConfigureAwait(false))
                {
                    return;
                }

                try
                {
                    await LoadLockedAsync().ConfigureAwait(false);
                }
                finally
                {
                    _fetchLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async Task AcceptAsync(ConfirmationViewModel item)
        {
            await ActAsync(item, true).ConfigureAwait(false);
        }

        public async Task DenyAsync(ConfirmationViewModel item)
        {
            await ActAsync(item, false).ConfigureAwait(false);
        }

        public void Close()
        {
            _closed = true;
            try
            {
                _lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task ActAsync(ConfirmationViewModel item, bool accept)
        {
            if (_closed || item == null || item.Confirmation == null || !item.TryBeginAction())
            {
                return;
            }

            try
            {
                bool succeeded = accept
                    ? await _actions.AcceptAsync(_account, item.Confirmation, _lifetime.Token).ConfigureAwait(false)
                    : await _actions.DenyAsync(_account, item.Confirmation, _lifetime.Token).ConfigureAwait(false);
                if (_closed)
                {
                    return;
                }

                if (!succeeded)
                {
                    StatusText = accept ? "Unable to accept confirmation." : "Unable to cancel confirmation.";
                    return;
                }

                await ReloadAfterActionAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (!_closed)
                {
                    StatusText = accept ? "Unable to accept confirmation." : "Unable to cancel confirmation.";
                }
            }
            finally
            {
                if (!_closed)
                {
                    item.EndAction();
                }
            }
        }

        private async Task ReloadAfterActionAsync()
        {
            await _fetchLock.WaitAsync(_lifetime.Token).ConfigureAwait(false);
            try
            {
                if (_closed)
                {
                    return;
                }

                await LoadLockedAsync().ConfigureAwait(false);
            }
            finally
            {
                _fetchLock.Release();
            }
        }

        private async Task LoadLockedAsync()
        {
            if (_closed)
            {
                return;
            }

            IsRefreshing = true;
            State = ConfirmationUiState.Loading;
            Confirmations = new ConfirmationViewModel[0];
            try
            {
                ConfirmationLoadResult result = await _loader.LoadAsync(_account, _persist, _lifetime.Token).ConfigureAwait(false);
                if (_closed)
                {
                    return;
                }

                Apply(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                if (!_closed)
                {
                    Apply(ConfirmationLoadResult.Failed("Unable to load confirmations."));
                }
            }
            finally
            {
                if (!_closed)
                {
                    IsRefreshing = false;
                }
            }
        }

        private void Apply(ConfirmationLoadResult result)
        {
            if (result == null)
            {
                result = ConfirmationLoadResult.Failed("Unable to load confirmations.");
            }

            if (result.Status == ConfirmationLoadStatus.Loaded)
            {
                State = ConfirmationUiState.Loaded;
                StatusText = "";
                Confirmations = CreateItems(result.Confirmations);
                StartIconLoads();
                return;
            }

            Confirmations = new ConfirmationViewModel[0];
            StatusText = result.Message;
            if (result.Status == ConfirmationLoadStatus.Empty)
            {
                State = ConfirmationUiState.Empty;
                return;
            }

            if (result.Status == ConfirmationLoadStatus.SessionExpired)
            {
                State = ConfirmationUiState.SessionExpired;
                return;
            }

            State = ConfirmationUiState.Failed;
        }

        private ConfirmationViewModel[] CreateItems(Confirmation[] confirmations)
        {
            if (confirmations == null || confirmations.Length == 0)
            {
                return new ConfirmationViewModel[0];
            }

            ConfirmationViewModel[] items = new ConfirmationViewModel[confirmations.Length];
            for (int i = 0; i < confirmations.Length; i++)
            {
                items[i] = new ConfirmationViewModel(confirmations[i]);
            }

            return items;
        }

        private void StartIconLoads()
        {
            if (_icons == null)
            {
                return;
            }

            IReadOnlyList<ConfirmationViewModel> items = Confirmations;
            for (int i = 0; i < items.Count; i++)
            {
                ConfirmationViewModel item = items[i];
                if (item == null || string.IsNullOrEmpty(item.IconUrl))
                {
                    continue;
                }

                LoadIcon(item);
            }
        }

        private async void LoadIcon(ConfirmationViewModel item)
        {
            try
            {
                byte[] data = await _icons.LoadAsync(item.IconUrl, _lifetime.Token).ConfigureAwait(false);
                if (_closed || data == null || data.Length == 0)
                {
                    return;
                }

                item.IconBytes = data;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
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
