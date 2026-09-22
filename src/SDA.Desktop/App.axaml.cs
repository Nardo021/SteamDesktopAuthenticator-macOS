using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SDA.Desktop.Views;
using System;

namespace SDA.Desktop
{
    public class App : Application, IApplicationShutdownHost
    {
        private IClassicDesktopStyleApplicationLifetime _desktop;
        private MainWindow _mainWindow;
        private TrayMenuService _tray;
        private bool _trayDisposed;

        public static ApplicationLifecycleService CurrentLifecycle { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktop = desktop;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                DelegatingWindowVisibilityHost windowHost = new DelegatingWindowVisibilityHost();
                CurrentLifecycle = new ApplicationLifecycleService(
                    windowHost,
                    this,
                    AppStartup.Current != null && AppStartup.Current.StartHiddenToTray);
                _mainWindow = new MainWindow(CurrentLifecycle);
                windowHost.Target = _mainWindow;
                desktop.MainWindow = _mainWindow;
                TrayMenuViewModel trayViewModel = new TrayMenuViewModel(new DesktopTrayMenuActions(CurrentLifecycle, () => _mainWindow));
                _tray = new TrayMenuService(trayViewModel);
                _tray.Attach(this);
                trayViewModel.Bind(_mainWindow.ViewModel);
                desktop.ShutdownRequested += OnShutdownRequested;
            }

            base.OnFrameworkInitializationCompleted();
        }

        public void StopBackgroundServices()
        {
            if (_mainWindow != null)
            {
                _mainWindow.StopBackgroundServices();
            }
        }

        public void CloseSecondaryWindows()
        {
            if (_mainWindow != null)
            {
                _mainWindow.CloseSecondaryWindows();
            }
        }

        public void DisposeTrayIcon()
        {
            if (_trayDisposed)
            {
                return;
            }

            _trayDisposed = true;
            if (_tray != null)
            {
                _tray.Dispose();
                _tray = null;
            }
        }

        public void Shutdown()
        {
            if (_desktop != null)
            {
                _desktop.Shutdown();
            }
        }

        private void OnShutdownRequested(object sender, ShutdownRequestedEventArgs e)
        {
            if (CurrentLifecycle != null)
            {
                CurrentLifecycle.HandleExternalShutdown();
            }
        }
    }
}
