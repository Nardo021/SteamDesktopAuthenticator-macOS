using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SDA.Platform.Mac;
using System;
using System.Threading;

namespace SDA.Desktop.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly TimerService _timer = new TimerService();
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
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
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
