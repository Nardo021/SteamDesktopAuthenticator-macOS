using System;
using System.Threading;

namespace SDA.Desktop.Services
{
    public enum ApplicationVisibilityState
    {
        Visible,
        HiddenToTray,
        ShuttingDown
    }

    public interface IWindowVisibilityHost
    {
        void ShowNormal();

        void HideWindow();

        void CloseMainWindow();
    }

    public interface IApplicationShutdownHost
    {
        void StopBackgroundServices();

        void CloseSecondaryWindows();

        void DisposeTrayIcon();

        void Shutdown();
    }

    public sealed class DelegatingWindowVisibilityHost : IWindowVisibilityHost
    {
        public IWindowVisibilityHost Target { get; set; }

        public void ShowNormal()
        {
            if (Target != null)
            {
                Target.ShowNormal();
            }
        }

        public void HideWindow()
        {
            if (Target != null)
            {
                Target.HideWindow();
            }
        }

        public void CloseMainWindow()
        {
            if (Target != null)
            {
                Target.CloseMainWindow();
            }
        }
    }

    public sealed class DelegatingApplicationShutdownHost : IApplicationShutdownHost
    {
        public IApplicationShutdownHost Target { get; set; }

        public void StopBackgroundServices()
        {
            if (Target != null)
            {
                Target.StopBackgroundServices();
            }
        }

        public void CloseSecondaryWindows()
        {
            if (Target != null)
            {
                Target.CloseSecondaryWindows();
            }
        }

        public void DisposeTrayIcon()
        {
            if (Target != null)
            {
                Target.DisposeTrayIcon();
            }
        }

        public void Shutdown()
        {
            if (Target != null)
            {
                Target.Shutdown();
            }
        }
    }

    public sealed class ApplicationLifecycleService
    {
        private readonly IWindowVisibilityHost _window;
        private readonly IApplicationShutdownHost _shutdown;
        private readonly bool _startHiddenToTray;
        private int _shutdownStarted;
        private int _hideInProgress;
        private int _unlockPromptDepth;

        public ApplicationLifecycleService(
            IWindowVisibilityHost window,
            IApplicationShutdownHost shutdown,
            bool startHiddenToTray)
        {
            _window = window ?? throw new ArgumentNullException("window");
            _shutdown = shutdown ?? throw new ArgumentNullException("shutdown");
            _startHiddenToTray = startHiddenToTray;
            State = startHiddenToTray
                ? ApplicationVisibilityState.HiddenToTray
                : ApplicationVisibilityState.Visible;
        }

        public event EventHandler StateChanged;

        public ApplicationVisibilityState State { get; private set; }

        public bool StartHiddenToTray
        {
            get { return _startHiddenToTray; }
        }

        public bool IsShuttingDown
        {
            get { return State == ApplicationVisibilityState.ShuttingDown; }
        }

        public bool IsHiddenToTray
        {
            get { return State == ApplicationVisibilityState.HiddenToTray; }
        }

        public bool UnlockPromptActive
        {
            get { return _unlockPromptDepth > 0; }
        }

        public int ShutdownCleanupCount { get; private set; }

        public void ApplyStartupVisibility()
        {
            if (IsShuttingDown)
            {
                return;
            }

            if (_startHiddenToTray)
            {
                HideMainWindow();
            }
            else
            {
                ShowMainWindow();
            }
        }

        public void ShowMainWindow()
        {
            if (IsShuttingDown)
            {
                return;
            }

            SetState(ApplicationVisibilityState.Visible);
            _window.ShowNormal();
        }

        public void HideMainWindow()
        {
            if (IsShuttingDown || _unlockPromptDepth > 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref _hideInProgress, 1) == 1)
            {
                return;
            }

            try
            {
                SetState(ApplicationVisibilityState.HiddenToTray);
                _window.HideWindow();
            }
            finally
            {
                Interlocked.Exchange(ref _hideInProgress, 0);
            }
        }

        public void HandleMinimized()
        {
            if (IsShuttingDown || _hideInProgress == 1 || _unlockPromptDepth > 0)
            {
                return;
            }

            HideMainWindow();
        }

        public void BeginUnlockPrompt()
        {
            if (IsShuttingDown)
            {
                return;
            }

            _unlockPromptDepth++;
            _window.ShowNormal();
        }

        public void EndUnlockPrompt()
        {
            if (_unlockPromptDepth > 0)
            {
                _unlockPromptDepth--;
            }

            if (_unlockPromptDepth == 0 && _startHiddenToTray && !IsShuttingDown)
            {
                HideMainWindow();
            }
        }

        public void HandleMainWindowClose()
        {
            Quit();
        }

        public void Quit()
        {
            if (!BeginShutdown())
            {
                return;
            }

            RunCleanup();
            _window.CloseMainWindow();
            _shutdown.Shutdown();
        }

        public void HandleExternalShutdown()
        {
            if (!BeginShutdown())
            {
                return;
            }

            RunCleanup();
        }

        private bool BeginShutdown()
        {
            return Interlocked.Exchange(ref _shutdownStarted, 1) == 0;
        }

        private void RunCleanup()
        {
            SetState(ApplicationVisibilityState.ShuttingDown);
            ShutdownCleanupCount++;
            _shutdown.StopBackgroundServices();
            _shutdown.CloseSecondaryWindows();
            _shutdown.DisposeTrayIcon();
        }

        private void SetState(ApplicationVisibilityState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            EventHandler handler = StateChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
