using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using SDA.Desktop.ViewModels;
using System;

namespace SDA.Desktop.Services
{
    public sealed class TrayMenuService : IDisposable
    {
        public const string ToolTipText = "Steam Desktop Authenticator";

        private readonly TrayMenuViewModel _viewModel;
        private readonly TrayIcon _icon;
        private readonly NativeMenu _menu = new NativeMenu();
        private bool _disposed;
        private bool _menuAssigned;

        public TrayMenuService(TrayMenuViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException("viewModel");
            _icon = new TrayIcon
            {
                ToolTipText = ToolTipText,
                Icon = LoadIcon(),
                IsVisible = true
            };
            _viewModel.Changed += OnViewModelChanged;
            RebuildMenu();
        }

        public TrayIcon Icon
        {
            get { return _icon; }
        }

        public TrayMenuViewModel ViewModel
        {
            get { return _viewModel; }
        }

        public void Attach(Application application)
        {
            if (application == null)
            {
                return;
            }

            TrayIcon.SetIcons(application, new TrayIcons { _icon });
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _viewModel.Changed -= OnViewModelChanged;
            _viewModel.Unbind();
            _icon.IsVisible = false;
            _icon.Dispose();
            Application application = Application.Current;
            if (application != null)
            {
                TrayIcon.SetIcons(application, new TrayIcons());
            }
        }

        private void OnViewModelChanged(object sender, EventArgs e)
        {
            Dispatch(RebuildMenu);
        }

        private void RebuildMenu()
        {
            _menu.Items.Clear();
            _menu.Add(CreateItem("Restore", true, () => _viewModel.Restore()));
            _menu.Add(new NativeMenuItemSeparator());
            _menu.Add(CreateAccountsItem());
            _menu.Add(CreateItem("View Confirmations", _viewModel.CanViewConfirmations, () => _viewModel.ViewConfirmations()));
            _menu.Add(CreateItem("Copy Steam Guard", _viewModel.CanCopy, () => { _ = _viewModel.CopySteamGuardAsync(); }));
            _menu.Add(new NativeMenuItemSeparator());
            _menu.Add(CreateItem("Quit", true, () => _viewModel.Quit()));
            if (!_menuAssigned)
            {
                _icon.Menu = _menu;
                _menuAssigned = true;
            }
        }

        private NativeMenuItem CreateAccountsItem()
        {
            NativeMenuItem accounts = new NativeMenuItem("Accounts");
            NativeMenu submenu = new NativeMenu();
            if (!_viewModel.HasAccounts)
            {
                NativeMenuItem empty = new NativeMenuItem(TrayMenuViewModel.NoAccountsText);
                empty.IsEnabled = false;
                submenu.Add(empty);
            }
            else
            {
                foreach (TrayAccountItem account in _viewModel.Accounts)
                {
                    TrayAccountItem captured = account;
                    NativeMenuItem item = new NativeMenuItem(captured.DisplayName);
                    item.ToggleType = MenuItemToggleType.CheckBox;
                    item.IsChecked = captured.IsSelected;
                    item.Click += (_, __) => Dispatch(() => _viewModel.SelectAccount(captured.Account));
                    submenu.Add(item);
                }
            }

            accounts.Menu = submenu;
            return accounts;
        }

        private static NativeMenuItem CreateItem(string header, bool enabled, Action action)
        {
            NativeMenuItem item = new NativeMenuItem(header);
            item.IsEnabled = enabled;
            item.Click += (_, __) => Dispatch(action);
            return item;
        }

        private static void Dispatch(Action action)
        {
            if (action == null)
            {
                return;
            }

            Dispatcher dispatcher = Dispatcher.UIThread;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Post(action);
        }

        private static WindowIcon LoadIcon()
        {
            try
            {
                return new WindowIcon(AssetLoader.Open(new Uri("avares://SDA.Desktop/Assets/icon.png")));
            }
            catch (Exception)
            {
                try
                {
                    return new WindowIcon(AssetLoader.Open(new Uri("avares://SDA.Desktop/Assets/icon.ico")));
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }
}
