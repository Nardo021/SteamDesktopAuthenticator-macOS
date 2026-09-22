using Avalonia.Controls;
using SDA.Desktop.ViewModels;
using SDA.Desktop.Views;
using SteamAuth;
using System;
using System.Collections.Generic;

namespace SDA.Desktop.Services
{
    public interface IConfirmationsPresenter
    {
        void ShowForSelectedAccount();

        void CloseAll();

        int ShowCalls { get; }

        SteamGuardAccount LastAccount { get; }
    }

    public sealed class ConfirmationsWindowCoordinator : IConfirmationsPresenter
    {
        private readonly Dictionary<string, ConfirmationsWindow> _windows = new Dictionary<string, ConfirmationsWindow>();
        private readonly Func<MainWindowViewModel> _viewModel;
        private readonly Func<Window> _owner;

        public ConfirmationsWindowCoordinator(Func<MainWindowViewModel> viewModel, Func<Window> owner)
        {
            _viewModel = viewModel;
            _owner = owner;
        }

        public int ShowCalls { get; private set; }

        public SteamGuardAccount LastAccount { get; private set; }

        public void ShowForSelectedAccount()
        {
            MainWindowViewModel viewModel = _viewModel == null ? null : _viewModel();
            if (viewModel == null || viewModel.SelectedAccount == null)
            {
                return;
            }

            ShowFor(viewModel.SelectedAccount.Account);
        }

        public void ShowFor(SteamGuardAccount account)
        {
            if (account == null)
            {
                return;
            }

            ShowCalls++;
            LastAccount = account;
            string key = ConfirmationWindowKey(account);
            ConfirmationsWindow existing;
            if (_windows.TryGetValue(key, out existing))
            {
                existing.Activate();
                return;
            }

            MainWindowViewModel viewModel = _viewModel == null ? null : _viewModel();
            ConfirmationsWindow window = null;
            window = new ConfirmationsWindow(
                account,
                new ConfirmationService(new SteamGuardAccountConfirmationClient()),
                (target, session) =>
                {
                    if (viewModel == null)
                    {
                        return System.Threading.Tasks.Task.FromResult("Unable to save refreshed session.");
                    }

                    return viewModel.PersistUpdatedSessionAsync(target, session, new EncryptionPrompt(window));
                });
            window.Closed += OnWindowClosed;
            _windows[key] = window;
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

        public void CloseAll()
        {
            ConfirmationsWindow[] windows = new ConfirmationsWindow[_windows.Count];
            _windows.Values.CopyTo(windows, 0);
            _windows.Clear();
            foreach (ConfirmationsWindow window in windows)
            {
                if (window == null)
                {
                    continue;
                }

                window.Closed -= OnWindowClosed;
                try
                {
                    window.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            ConfirmationsWindow window = sender as ConfirmationsWindow;
            if (window == null)
            {
                return;
            }

            window.Closed -= OnWindowClosed;
            string found = null;
            foreach (KeyValuePair<string, ConfirmationsWindow> pair in _windows)
            {
                if (pair.Value == window)
                {
                    found = pair.Key;
                    break;
                }
            }

            if (found != null)
            {
                _windows.Remove(found);
            }
        }

        private static string ConfirmationWindowKey(SteamGuardAccount account)
        {
            if (account != null && account.Session != null && account.Session.SteamID != 0)
            {
                return account.Session.SteamID.ToString();
            }

            if (account != null && !string.IsNullOrEmpty(account.AccountName))
            {
                return "name:" + account.AccountName;
            }

            return "account";
        }
    }
}
