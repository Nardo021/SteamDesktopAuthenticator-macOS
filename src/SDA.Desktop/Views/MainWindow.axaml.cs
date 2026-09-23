using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SDA.Desktop;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SDA.Platform.Mac;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Views
{
    public partial class MainWindow : Window, IWindowVisibilityHost
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly TimerService _timer = new TimerService();
        private readonly ApplicationLifecycleService _lifecycle;
        private readonly ConfirmationsWindowCoordinator _confirmations;
        private readonly MainWindowMenu _menu = new MainWindowMenu();
        private SetupAccountWindow _setupWindow;
        private ImportAccountWindow _importWindow;
        private readonly AuthenticatorDeactivationService _deactivation = new AuthenticatorDeactivationService();
        private readonly ISteamGuardCodeGenerator _codes = new SteamAuthGuardCodeGenerator();
        private readonly ConfirmationPopupViewModel _popupViewModel;
        private readonly PeriodicConfirmationService _periodic;
        private readonly BackgroundConfirmationCoordinator _coordinator;
        private bool _timerStarted;
        private bool _servicesStopped;
        private bool _suppressMinimize;
        private bool _closingFromLifecycle;

        public MainWindow()
            : this(App.CurrentLifecycle)
        {
        }

        public MainWindow(ApplicationLifecycleService lifecycle)
        {
            InitializeComponent();
            _lifecycle = lifecycle;
            MacPlatformServices.EnsureDefaultStorage();
            _viewModel = new MainWindowViewModel(
                new AccountService(),
                new SettingsService(MacAppPaths.GetSettingsFilePath()),
                new SteamAuthClock(),
                new EncryptionPrompt(this, _lifecycle),
                new FolderPicker(() => this),
                new ClipboardService(() => this),
                MacAppPaths.GetDefaultMaFilesDirectory(),
                null,
                null,
                AppStartup.Current == null ? null : AppStartup.Current.EncryptionKey);
            DataContext = _viewModel;
            _confirmations = new ConfirmationsWindowCoordinator(() => _viewModel, () => this);
            if (_lifecycle != null && _lifecycle.StartHiddenToTray)
            {
                ShowActivated = false;
                Opacity = 0;
            }
            _menu.CheckUpdatesAction = CheckForUpdates;
            _menu.OpenFolderItem.Click += OnOpenFolderMenuClick;
            _menu.ImportAccountItem.Click += OnImportAccountClick;
            _menu.CopyCodeItem.Click += OnCopyMenuClick;
            _menu.SetupNewAccountItem.Click += OnSetupNewAccountClick;
            _menu.LoginAgainItem.Click += OnLoginAgainClick;
            _menu.ForceRefreshItem.Click += OnForceRefreshClick;
            _menu.ViewConfirmationsItem.Click += OnViewConfirmationsClick;
            _menu.RemoveFromManifestItem.Click += OnRemoveFromManifestClick;
            _menu.DeactivateItem.Click += OnDeactivateAuthenticatorClick;
            _menu.EncryptionItem.Click += OnManageEncryptionClick;
            _menu.ShowMainWindowItem.Click += OnShowMainWindowClick;
            IConfirmationClient confirmationClient = new SteamGuardAccountConfirmationClient();
            ConfirmationService confirmationService = new ConfirmationService(confirmationClient);
            _popupViewModel = new ConfirmationPopupViewModel(confirmationClient);
            _periodic = new PeriodicConfirmationService(
                confirmationService,
                confirmationClient,
                SnapshotAccountsForPoll,
                (account, session) => _viewModel.PersistUpdatedSessionAsync(account, session, SilentEncryptionPrompt.Instance),
                _popupViewModel);
            _periodic.StatusRaised += OnPeriodicStatus;
            _coordinator = new BackgroundConfirmationCoordinator(_periodic, _popupViewModel, () => this);
            _viewModel.ManifestContextChanged += OnManifestContextChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _menu.Attach(this);
            UpdateSessionMenu();
        }

        public MainWindowViewModel ViewModel
        {
            get { return _viewModel; }
        }

        public IConfirmationsPresenter Confirmations
        {
            get { return _confirmations; }
        }

        public void ShowAbout()
        {
            RunOnUi(() => _ = ShowAboutAsync());
        }

        public void CheckForUpdates()
        {
            RunOnUi(() => _ = CheckForUpdatesAsync(true));
        }

        public void ShowSettings()
        {
            RunOnUi(() => OnSettingsClick(this, EventArgs.Empty));
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            _menu.Attach(this);

            if (_lifecycle != null)
            {
                _lifecycle.ApplyStartupVisibility();
            }

            Dispatcher.UIThread.Post(StartAsync, DispatcherPriority.Background);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (_lifecycle == null || _lifecycle.IsShuttingDown || _closingFromLifecycle)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            _lifecycle.HandleMainWindowClose();
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
            ApplyPeriodicSettings();
            if (_lifecycle == null || !_lifecycle.IsHiddenToTray)
            {
                _ = CheckForUpdatesAsync(false);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            StopBackgroundServices();
            base.OnClosed(e);
        }

        protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == Window.WindowStateProperty
                && WindowState == WindowState.Minimized
                && !_suppressMinimize
                && _lifecycle != null
                && !_lifecycle.IsShuttingDown)
            {
                _lifecycle.HandleMinimized();
            }
        }

        private async void OnTick(object sender, EventArgs e)
        {
            await _viewModel.RefreshCodesAsync();
        }

        private async void OnCopyClick(object sender, RoutedEventArgs e)
        {
            await _viewModel.CopyAsync();
        }

        private async void OnVersionClick(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(true);
        }

        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                await Dispatcher.UIThread.InvokeAsync(() => CheckForUpdatesAsync(manual));
                return;
            }

            if (manual)
            {
                EnsureMainWindowVisible();
            }

            try
            {
                UpdateCheckService checker = new UpdateCheckService(AppVersion.Informational, new GitHubUpdateReleaseSource());
                UpdateCheckResult result = await checker.CheckAsync();
                if (result.Status == UpdateCheckStatus.UpdateAvailable)
                {
                    ConfirmWindow dialog = new ConfirmWindow(
                        "New Version",
                        "A new version is available.\nYou will update from version "
                        + result.CurrentVersion
                        + " to "
                        + result.LatestVersion
                        + ".\nOpen the macOS release page?",
                        "Open",
                        "Later");
                    bool? open = await dialog.ShowDialog<bool>(this);
                    if (open == true)
                    {
                        ReleasePageOpener.TryOpen(result.ReleasePageUrl);
                    }
                }
                else if (manual && result.Status == UpdateCheckStatus.Current)
                {
                    MessageWindow message = new MessageWindow(
                        "Steam Desktop Authenticator",
                        "You are using the latest version: " + result.CurrentVersion);
                    await message.ShowDialog(this);
                }
                else if (manual && result.Status == UpdateCheckStatus.Unknown)
                {
                    MessageWindow message = new MessageWindow(
                        "Steam Desktop Authenticator",
                        "Failed to check for updates. Check the connection and try again.");
                    await message.ShowDialog(this);
                }
            }
            catch (Exception)
            {
                if (manual)
                {
                    MessageWindow message = new MessageWindow(
                        "Steam Desktop Authenticator",
                        "Failed to check for updates. Check the connection and try again.");
                    await message.ShowDialog(this);
                }
            }
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
                _viewModel.SetFailureStatus("Steam login failed. Check the password and try again.");
            }
            finally
            {
                _viewModel.EndSessionOperation();
                if (account != null)
                {
                    _periodic.ForgetSessionExpired(account.AccountName);
                }
            }
        }

        private async void OnSettingsClick(object sender, EventArgs e)
        {
            EnsureMainWindowVisible();
            SettingsWindowViewModel settings = new SettingsWindowViewModel(
                new ManifestSettingsService(),
                _viewModel.CurrentDirectory);
            SettingsWindow window = new SettingsWindow(settings);
            bool saved = await window.ShowDialog<bool>(this);
            if (saved)
            {
                ApplyPeriodicSettings();
            }
        }

        private void OnSetupNewAccountButtonClick(object sender, RoutedEventArgs e)
        {
            OnSetupNewAccountClick(sender, e);
        }

        private void OnViewConfirmationsButtonClick(object sender, RoutedEventArgs e)
        {
            OnViewConfirmationsClick(sender, e);
        }

        private void OnManageEncryptionButtonClick(object sender, RoutedEventArgs e)
        {
            OnManageEncryptionClick(sender, e);
        }

        private async void OnSetupNewAccountClick(object sender, EventArgs e)
        {
            if (_setupWindow != null)
            {
                _setupWindow.Activate();
                return;
            }

            SetSetupNewAccountEnabled(false);
            SetupAccountWindow window = new SetupAccountWindow(
                new SteamLoginService(),
                new SteamAuthAuthenticatorLinkerFactory(),
                new EnrollmentPersistenceService(),
                _viewModel.CurrentDirectory,
                _viewModel.CurrentPassKey);
            _setupWindow = window;
            window.Closed += OnSetupWindowClosed;
            try
            {
                bool completed = await window.ShowDialog<bool>(this);
                if (completed)
                {
                    await _viewModel.ReloadAfterEnrollmentAsync(window.ViewModel.EnrolledAccountName, window.ViewModel.UsedPassKey);
                }
            }
            catch (Exception)
            {
                _viewModel.SetFailureStatus("Unable to set up a new account. Check the Steam login and try again.");
            }
        }

        private void OnSetupWindowClosed(object sender, EventArgs e)
        {
            SetupAccountWindow window = sender as SetupAccountWindow;
            if (window != null)
            {
                window.Closed -= OnSetupWindowClosed;
            }

            _setupWindow = null;
            SetSetupNewAccountEnabled(true);
        }

        private void OnViewConfirmationsClick(object sender, EventArgs e)
        {
            _confirmations.ShowForSelectedAccount();
        }

        private async void OnForceRefreshClick(object sender, EventArgs e)
        {
            try
            {
                await _viewModel.ForceRefreshAsync();
            }
            catch (Exception)
            {
                _viewModel.SetFailureStatus("Unable to refresh session. Check the connection and try again.");
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CanUseSessionActions)
                || e.PropertyName == nameof(MainWindowViewModel.SelectedAccount)
                || e.PropertyName == nameof(MainWindowViewModel.CanManageEncryption)
                || e.PropertyName == nameof(MainWindowViewModel.EncryptionMenuText)
                || e.PropertyName == nameof(MainWindowViewModel.CanCopyCode)
                || string.IsNullOrEmpty(e.PropertyName))
            {
                UpdateSessionMenu();
            }
        }

        private void UpdateSessionMenu()
        {
            _menu.Update(_viewModel);
        }

        private void SetSetupNewAccountEnabled(bool enabled)
        {
            _menu.SetupNewAccountItem.IsEnabled = enabled;
            if (SetupNewAccountButton != null)
            {
                SetupNewAccountButton.IsEnabled = enabled;
            }
        }

        private async Task ShowAboutAsync()
        {
            EnsureMainWindowVisible();
            await ShowMessageAsync(
                "About Steam Desktop Authenticator",
                "Steam Desktop Authenticator\nVersion "
                + AppVersion.Informational
                + "\n\nUnsigned macOS port. Apple has not verified the developer identity.\n\n"
                + "https://github.com/Nardo021/SteamDesktopAuthenticator-macOS");
        }

        private static void RunOnUi(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.UIThread.Post(action);
        }

        private void OnShowMainWindowClick(object sender, EventArgs e)
        {
            EnsureMainWindowVisible();
        }

        private async void OnCopyMenuClick(object sender, EventArgs e)
        {
            await _viewModel.CopyAsync();
        }

        private async void OnOpenFolderMenuClick(object sender, EventArgs e)
        {
            await _viewModel.OpenFolderAsync();
        }

        private void EnsureMainWindowVisible()
        {
            if (_lifecycle != null)
            {
                _lifecycle.ShowMainWindow();
            }
        }

        private async void OnImportAccountClick(object sender, EventArgs e)
        {
            if (_importWindow != null)
            {
                _importWindow.Activate();
                return;
            }

            ImportAccountWindow window = new ImportAccountWindow(
                new AccountImportService(),
                new MaFilePicker(() => this),
                new SteamLoginService(),
                _viewModel.CurrentDirectory);
            _importWindow = window;
            window.Closed += OnImportWindowClosed;
            try
            {
                bool imported = await window.ShowDialog<bool>(this);
                if (imported && window.ViewModel.ImportedAccount != null)
                {
                    ulong steamId = window.ViewModel.ImportedAccount.Session == null ? 0 : window.ViewModel.ImportedAccount.Session.SteamID;
                    await _viewModel.ReloadPreservingSelectionAsync(window.ViewModel.ImportedAccount.AccountName, steamId);
                    _viewModel.SetFailureStatus(window.ViewModel.SuccessMessage ?? AccountImportService.SuccessMessage);
                }
            }
            catch (Exception)
            {
                _viewModel.SetFailureStatus("Unable to import account. Check the maFile and try again.");
            }
        }

        private void OnImportWindowClosed(object sender, EventArgs e)
        {
            ImportAccountWindow window = sender as ImportAccountWindow;
            if (window != null)
            {
                window.Closed -= OnImportWindowClosed;
            }

            _importWindow = null;
        }

        private async void OnRemoveFromManifestClick(object sender, EventArgs e)
        {
            if (_viewModel.SelectedAccount == null)
            {
                return;
            }

            ManifestAccountRemovalService removal = new ManifestAccountRemovalService();
            SteamGuardAccount account = _viewModel.SelectedAccount.Account;
            if (_viewModel.ManifestEncrypted)
            {
                await ShowMessageAsync("Remove from Manifest", ManifestAccountRemovalService.EncryptedBlockedMessage);
                return;
            }

            ConfirmWindow confirm = new ConfirmWindow(
                "Remove from Manifest",
                ManifestAccountRemovalService.ConfirmationMessage,
                "Remove",
                "Cancel");
            if (!await confirm.ShowDialog<bool>(this))
            {
                return;
            }

            ManifestRemovalResult result = removal.RemoveFromManifest(account, _viewModel.CurrentDirectory);
            if (result.Status == ManifestRemovalStatus.Removed)
            {
                await _viewModel.ReloadPreservingSelectionAsync(null, 0);
            }

            await ShowMessageAsync("Remove from Manifest", result.Message);
        }

        private async void OnDeactivateAuthenticatorClick(object sender, EventArgs e)
        {
            if (_viewModel.SelectedAccount == null || !_viewModel.TryBeginSessionOperation())
            {
                return;
            }

            SteamGuardAccount account = _viewModel.SelectedAccount.Account;
            try
            {
                DeactivationResult prepared = await _deactivation.PrepareSessionAsync(
                    account,
                    _viewModel.CurrentDirectory,
                    _viewModel.CurrentPassKey,
                    CancellationToken.None);
                if (prepared.Status != DeactivationStatus.Succeeded)
                {
                    await ShowMessageAsync("Deactivate Authenticator", prepared.Message);
                    return;
                }

                DeactivateChoiceWindow choice = new DeactivateChoiceWindow(account.AccountName);
                if (!await choice.ShowDialog<bool>(this) || choice.Scheme == 0)
                {
                    await ShowMessageAsync("Deactivate Authenticator", AuthenticatorDeactivationService.CancelledMessage);
                    return;
                }

                string expected = _codes.Generate(account);
                DeactivateConfirmCodeWindow confirm = new DeactivateConfirmCodeWindow(account.AccountName, expected);
                if (!await confirm.ShowDialog<bool>(this))
                {
                    return;
                }

                if (!_deactivation.ConfirmationMatches(expected, confirm.EnteredCode))
                {
                    await ShowMessageAsync("Deactivate Authenticator", AuthenticatorDeactivationService.ConfirmationMismatchMessage);
                    return;
                }

                DeactivationResult remote = await _deactivation.DeactivateRemoteAsync(account, choice.Scheme, CancellationToken.None);
                if (!remote.RemoteSucceeded)
                {
                    await ShowMessageAsync("Deactivate Authenticator", remote.Message);
                    return;
                }

                await ShowMessageAsync("Deactivate Authenticator", AuthenticatorDeactivationService.SuccessAcknowledgement(choice.Scheme));
                DeactivationResult local = _deactivation.CleanupLocalAfterRemoteSuccess(account, _viewModel.CurrentDirectory, choice.Scheme);
                if (local.Status != DeactivationStatus.Succeeded)
                {
                    await ShowMessageAsync("Deactivate Authenticator", local.Message);
                }

                await _viewModel.ReloadPreservingSelectionAsync(null, 0);
            }
            catch (Exception)
            {
                await ShowMessageAsync("Deactivate Authenticator", AuthenticatorDeactivationService.RemoteFailedMessage);
            }
            finally
            {
                _viewModel.EndSessionOperation();
            }
        }

        private async void OnManageEncryptionClick(object sender, EventArgs e)
        {
            if (!_viewModel.CanManageEncryption)
            {
                return;
            }

            EncryptionManagementService encryption = new EncryptionManagementService();
            EncryptionManagementKind kind = encryption.Inspect(_viewModel.CurrentDirectory);
            if (kind == EncryptionManagementKind.Empty)
            {
                await ShowMessageAsync("Encryption", EncryptionManagementService.EmptyManifestMessage);
                return;
            }

            EncryptionManagementWindow window = new EncryptionManagementWindow(encryption, _viewModel.CurrentDirectory, kind);
            bool changed = await window.ShowDialog<bool>(this);
            if (!changed)
            {
                return;
            }

            _viewModel.SetActivePassKey(window.ViewModel.ActivePassKey);
            ulong steamId = 0;
            if (_viewModel.SelectedAccount != null && _viewModel.SelectedAccount.Account != null && _viewModel.SelectedAccount.Account.Session != null)
            {
                steamId = _viewModel.SelectedAccount.Account.Session.SteamID;
            }

            await _viewModel.ReloadPreservingSelectionAsync(null, steamId);
        }

        private Task ShowMessageAsync(string title, string message)
        {
            MessageWindow window = new MessageWindow(title, message);
            return window.ShowDialog<bool>(this);
        }

        public void ShowNormal()
        {
            _suppressMinimize = true;
            try
            {
                Opacity = 1;
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
            finally
            {
                _suppressMinimize = false;
            }
        }

        public void HideWindow()
        {
            _suppressMinimize = true;
            try
            {
                Hide();
                WindowState = WindowState.Normal;
                Opacity = 1;
            }
            finally
            {
                _suppressMinimize = false;
            }
        }

        public void CloseMainWindow()
        {
            if (_closingFromLifecycle)
            {
                return;
            }

            _closingFromLifecycle = true;
            Close();
        }

        public void StopBackgroundServices()
        {
            if (_servicesStopped)
            {
                return;
            }

            _servicesStopped = true;
            _timer.Tick -= OnTick;
            _timer.Dispose();
            _viewModel.ManifestContextChanged -= OnManifestContextChanged;
            _periodic.StatusRaised -= OnPeriodicStatus;
            _coordinator.Dispose();
        }

        public void CloseSecondaryWindows()
        {
            _confirmations.CloseAll();
            if (_setupWindow != null)
            {
                try
                {
                    _setupWindow.Close();
                }
                catch (Exception)
                {
                }
            }

            if (_importWindow != null)
            {
                try
                {
                    _importWindow.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        private async void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Source is TextBox)
            {
                return;
            }

            if (e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            {
                await _viewModel.CopyAsync();
                e.Handled = true;
                return;
            }

            bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            bool command = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            AccountMoveDirection direction;
            if (AccountReorderShortcuts.TryResolve(control, command, e.Key == Key.Up, e.Key == Key.Down, out direction))
            {
                e.Handled = true;
                await _viewModel.TryMoveSelectedAsync(direction);
                return;
            }

            string typed = CharacterFromKey(e.Key, e.KeyModifiers);
            if (typed != null && AccountSearchKeyboard.ShouldFocusSearch(false, control || command, typed[0]))
            {
                AccountSearchBox.Focus();
                _viewModel.SearchText = (_viewModel.SearchText ?? "") + typed;
                e.Handled = true;
            }
        }

        private PeriodicPollSnapshot SnapshotAccountsForPoll()
        {
            List<SteamGuardAccount> accounts = new List<SteamGuardAccount>();
            ManifestRuntimeSettings settings = _periodic.Settings;
            if (settings.CheckAllAccounts)
            {
                IReadOnlyList<AccountViewModel> all = _viewModel.AllAccounts;
                if (all != null)
                {
                    foreach (AccountViewModel item in all)
                    {
                        if (item != null && item.Account != null)
                        {
                            accounts.Add(item.Account);
                        }
                    }
                }
            }
            else if (_viewModel.SelectedAccount != null && _viewModel.SelectedAccount.Account != null)
            {
                accounts.Add(_viewModel.SelectedAccount.Account);
            }

            return new PeriodicPollSnapshot(accounts, _viewModel.CurrentDirectory, _viewModel.CurrentPassKey);
        }

        private void ApplyPeriodicSettings()
        {
            _coordinator.ApplySettings(_viewModel.ReadRuntimeSettings());
        }

        private void OnManifestContextChanged(object sender, EventArgs e)
        {
            _periodic.ClearSessionExpiredSuppression();
            ApplyPeriodicSettings();
        }

        private void OnPeriodicStatus(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Dispatcher.UIThread.Post(() => _viewModel.SetFailureStatus(message));
        }

        private static string CharacterFromKey(Key key, KeyModifiers modifiers)
        {
            if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta) || modifiers.HasFlag(KeyModifiers.Alt))
            {
                return null;
            }

            if (key >= Key.A && key <= Key.Z)
            {
                char letter = (char)('a' + (key - Key.A));
                if (modifiers.HasFlag(KeyModifiers.Shift))
                {
                    letter = char.ToUpperInvariant(letter);
                }

                return letter.ToString();
            }

            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((char)('0' + (key - Key.D0))).ToString();
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                return ((char)('0' + (key - Key.NumPad0))).ToString();
            }

            return null;
        }
    }
}
