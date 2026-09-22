using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SDA.Platform.Mac;
using SteamAuth;
using System;
using System.ComponentModel;
using System.Threading;

namespace SDA.Desktop.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly TimerService _timer = new TimerService();
        private NativeMenuItem _loginAgainItem;
        private NativeMenuItem _forceRefreshItem;
        private NativeMenu _sessionMenu;
        private bool _timerStarted;

        public MainWindow()
        {
            InitializeComponent();
            MacPlatformServices.EnsureDefaultStorage();
            _viewModel = new MainWindowViewModel(
                new AccountService(),
                new SettingsService(MacAppPaths.GetSettingsFilePath()),
                new SteamAuthClock(),
                new EncryptionPrompt(this),
                new FolderPicker(() => this),
                new ClipboardService(() => this),
                MacAppPaths.GetDefaultMaFilesDirectory());
            DataContext = _viewModel;
            _loginAgainItem = new NativeMenuItem("Login Again");
            _forceRefreshItem = new NativeMenuItem("Force Session Refresh");
            _loginAgainItem.Click += OnLoginAgainClick;
            _forceRefreshItem.Click += OnForceRefreshClick;
            NativeMenu accountMenu = new NativeMenu();
            accountMenu.Add(_loginAgainItem);
            accountMenu.Add(_forceRefreshItem);
            NativeMenuItem accountItem = new NativeMenuItem("Selected Account");
            accountItem.Menu = accountMenu;
            _sessionMenu = new NativeMenu();
            _sessionMenu.Add(accountItem);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateSessionMenu();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (_sessionMenu != null && NativeMenu.GetMenu(this) != _sessionMenu)
            {
                NativeMenu.SetMenu(this, _sessionMenu);
            }

            Dispatcher.UIThread.Post(StartAsync, DispatcherPriority.Background);
        }

        private async void StartAsync()
        {
            await _viewModel.InitializeAsync();
            if (_timerStarted)
            {
                return;
            }

            _timerStarted = true;
            _timer.Tick += OnTick;
            _timer.Start(SynchronizationContext.Current);
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Tick -= OnTick;
            _timer.Dispose();
            base.OnClosed(e);
        }

        private async void OnTick(object sender, EventArgs e)
        {
            await _viewModel.RefreshCodesAsync();
        }

        private async void OnCopyClick(object sender, RoutedEventArgs e)
        {
            await _viewModel.CopyAsync();
        }

        private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            await _viewModel.OpenFolderAsync();
        }

        private async void OnLoginAgainClick(object sender, EventArgs e)
        {
            if (!_viewModel.TryBeginSessionOperation())
            {
                return;
            }

            SteamGuardAccount account = _viewModel.SelectedAccount.Account;
            try
            {
                LoginWindow window = new LoginWindow(account, new SteamLoginService(), _viewModel.CommitRefreshedSessionAsync);
                await window.ShowDialog<bool>(this);
            }
            catch (Exception)
            {
                _viewModel.SetFailureStatus("Steam login failed.");
            }
            finally
            {
                _viewModel.EndSessionOperation();
            }
        }

        private async void OnForceRefreshClick(object sender, EventArgs e)
        {
            try
            {
                await _viewModel.ForceRefreshAsync();
            }
            catch (Exception)
            {
                _viewModel.SetFailureStatus("Unable to refresh session.");
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CanUseSessionActions)
                || e.PropertyName == nameof(MainWindowViewModel.SelectedAccount)
                || string.IsNullOrEmpty(e.PropertyName))
            {
                UpdateSessionMenu();
            }
        }

        private void UpdateSessionMenu()
        {
            bool enabled = _viewModel.CanUseSessionActions;
            _loginAgainItem.IsEnabled = enabled;
            _forceRefreshItem.IsEnabled = enabled;
        }

        private async void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                await _viewModel.CopyAsync();
                e.Handled = true;
            }
        }
    }
}
