using SDA.Desktop.ViewModels;
using SDA.Desktop.Views;
using System;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public sealed class DesktopTrayMenuActions : ITrayMenuActions
    {
        private readonly ApplicationLifecycleService _lifecycle;
        private readonly Func<MainWindow> _window;

        public DesktopTrayMenuActions(ApplicationLifecycleService lifecycle, Func<MainWindow> window)
        {
            _lifecycle = lifecycle;
            _window = window;
        }

        public void Restore()
        {
            _lifecycle.ShowMainWindow();
        }

        public void SelectAccount(AccountViewModel account)
        {
            MainWindow window = _window == null ? null : _window();
            if (window == null || account == null)
            {
                return;
            }

            window.ViewModel.SelectedAccount = account;
        }

        public Task CopySteamGuardAsync()
        {
            MainWindow window = _window == null ? null : _window();
            if (window == null)
            {
                return Task.CompletedTask;
            }

            return window.ViewModel.CopyAsync();
        }

        public void ViewConfirmations()
        {
            MainWindow window = _window == null ? null : _window();
            if (window == null)
            {
                return;
            }

            window.Confirmations.ShowForSelectedAccount();
        }

        public void Quit()
        {
            _lifecycle.Quit();
        }
    }
}
