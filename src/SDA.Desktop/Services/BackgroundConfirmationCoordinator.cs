using Avalonia.Controls;
using Avalonia.Threading;
using SDA.Desktop.ViewModels;
using SDA.Desktop.Views;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public sealed class BackgroundConfirmationCoordinator : IDisposable
    {
        private readonly PeriodicConfirmationService _service;
        private readonly ConfirmationPopupViewModel _popup;
        private readonly Func<Window> _owner;
        private readonly object _sync = new object();
        private Timer _timer;
        private ConfirmationPopupWindow _window;
        private bool _disposed;

        public BackgroundConfirmationCoordinator(
            PeriodicConfirmationService service,
            ConfirmationPopupViewModel popup,
            Func<Window> owner)
        {
            _service = service ?? throw new ArgumentNullException("service");
            _popup = popup ?? throw new ArgumentNullException("popup");
            _owner = owner;
            _popup.VisibilityChanged += OnPopupVisibilityChanged;
        }

        public void ApplySettings(ManifestRuntimeSettings settings)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _service.ApplySettings(settings);
                StopTimerUnlocked();
                if (settings != null && settings.PeriodicChecking)
                {
                    int milliseconds = settings.EffectiveIntervalSeconds * 1000;
                    _timer = new Timer(OnTick, null, milliseconds, milliseconds);
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                StopTimerUnlocked();
                _popup.VisibilityChanged -= OnPopupVisibilityChanged;
            }

            ClosePopupWindow();
        }

        private void OnTick(object state)
        {
            Dispatcher dispatcher = Dispatcher.UIThread;
            if (dispatcher == null)
            {
                _ = PollSafe();
                return;
            }

            dispatcher.Post(() => { _ = PollSafe(); });
        }

        private async Task PollSafe()
        {
            try
            {
                await _service.PollAsync();
            }
            catch (Exception)
            {
            }
        }

        private void StopTimerUnlocked()
        {
            Timer timer = _timer;
            _timer = null;
            if (timer != null)
            {
                timer.Dispose();
            }
        }

        private void OnPopupVisibilityChanged(object sender, EventArgs e)
        {
            Dispatcher dispatcher = Dispatcher.UIThread;
            if (dispatcher == null)
            {
                SyncPopupWindow();
                return;
            }

            dispatcher.Post(SyncPopupWindow);
        }

        private void SyncPopupWindow()
        {
            if (_popup.IsVisible)
            {
                if (_window == null)
                {
                    ConfirmationPopupWindow window = new ConfirmationPopupWindow(_popup);
                    window.Closed += OnPopupWindowClosed;
                    _window = window;
                    Window owner = _owner == null ? null : _owner();
                    if (owner != null)
                    {
                        window.Show(owner);
                    }
                    else
                    {
                        window.Show();
                    }
                }
            }
            else
            {
                ClosePopupWindow();
            }
        }

        private void OnPopupWindowClosed(object sender, EventArgs e)
        {
            ConfirmationPopupWindow window = sender as ConfirmationPopupWindow;
            if (window != null)
            {
                window.Closed -= OnPopupWindowClosed;
            }

            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        }

        private void ClosePopupWindow()
        {
            ConfirmationPopupWindow window = _window;
            _window = null;
            if (window == null)
            {
                return;
            }

            window.Closed -= OnPopupWindowClosed;
            try
            {
                window.Close();
            }
            catch (Exception)
            {
            }
        }
    }
}
