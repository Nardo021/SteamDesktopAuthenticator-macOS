using Avalonia.Controls;
using Avalonia.Input;
using SDA.Desktop.ViewModels;
using System;
using System.Windows.Input;

namespace SDA.Desktop.Views
{
    public sealed class MainWindowMenu
    {
        public MainWindowMenu()
        {
            CheckUpdatesItem = new NativeMenuItem("Check for Updates...")
            {
                Command = new AlwaysEnabledCommand(() =>
                {
                    if (CheckUpdatesAction != null)
                    {
                        CheckUpdatesAction();
                    }
                })
            };

            OpenFolderItem = new NativeMenuItem("Open maFiles Folder...")
            {
                Gesture = new KeyGesture(Key.O, KeyModifiers.Meta)
            };
            ImportAccountItem = new NativeMenuItem("Import Account...")
            {
                Gesture = new KeyGesture(Key.I, KeyModifiers.Meta | KeyModifiers.Shift)
            };
            NativeMenu fileMenu = new NativeMenu();
            fileMenu.Add(OpenFolderItem);
            fileMenu.Add(ImportAccountItem);
            FileItem = new NativeMenuItem("File") { Menu = fileMenu };

            CopyCodeItem = new NativeMenuItem("Copy Code");
            NativeMenu editMenu = new NativeMenu();
            editMenu.Add(CopyCodeItem);
            EditItem = new NativeMenuItem("Edit") { Menu = editMenu };

            SetupNewAccountItem = new NativeMenuItem("Setup New Account...");
            LoginAgainItem = new NativeMenuItem("Login Again...");
            ForceRefreshItem = new NativeMenuItem("Force Session Refresh");
            ViewConfirmationsItem = new NativeMenuItem("View Confirmations");
            RemoveFromManifestItem = new NativeMenuItem("Remove from Manifest...");
            DeactivateItem = new NativeMenuItem("Deactivate Authenticator...");
            EncryptionItem = new NativeMenuItem("Setup Encryption...");
            NativeMenu accountMenu = new NativeMenu();
            accountMenu.Add(SetupNewAccountItem);
            accountMenu.Add(new NativeMenuItemSeparator());
            accountMenu.Add(LoginAgainItem);
            accountMenu.Add(ForceRefreshItem);
            accountMenu.Add(ViewConfirmationsItem);
            accountMenu.Add(new NativeMenuItemSeparator());
            accountMenu.Add(RemoveFromManifestItem);
            accountMenu.Add(DeactivateItem);
            accountMenu.Add(new NativeMenuItemSeparator());
            accountMenu.Add(EncryptionItem);
            AccountItem = new NativeMenuItem("Account") { Menu = accountMenu };

            ShowMainWindowItem = new NativeMenuItem("Steam Desktop Authenticator");
            NativeMenu windowMenu = new NativeMenu();
            windowMenu.Add(ShowMainWindowItem);
            WindowItem = new NativeMenuItem("Window") { Menu = windowMenu };

            NativeMenu helpMenu = new NativeMenu();
            helpMenu.Add(CheckUpdatesItem);
            HelpItem = new NativeMenuItem("Help") { Menu = helpMenu };

            SessionMenu = new NativeMenu();
            SessionMenu.Add(FileItem);
            SessionMenu.Add(EditItem);
            SessionMenu.Add(AccountItem);
            SessionMenu.Add(WindowItem);
            SessionMenu.Add(HelpItem);
        }

        public Action CheckUpdatesAction { get; set; }

        public NativeMenu SessionMenu { get; }

        public NativeMenuItem CheckUpdatesItem { get; }

        public NativeMenuItem FileItem { get; }

        public NativeMenuItem OpenFolderItem { get; }

        public NativeMenuItem ImportAccountItem { get; }

        public NativeMenuItem EditItem { get; }

        public NativeMenuItem CopyCodeItem { get; }

        public NativeMenuItem AccountItem { get; }

        public NativeMenuItem SetupNewAccountItem { get; }

        public NativeMenuItem LoginAgainItem { get; }

        public NativeMenuItem ForceRefreshItem { get; }

        public NativeMenuItem ViewConfirmationsItem { get; }

        public NativeMenuItem RemoveFromManifestItem { get; }

        public NativeMenuItem DeactivateItem { get; }

        public NativeMenuItem EncryptionItem { get; }

        public NativeMenuItem WindowItem { get; }

        public NativeMenuItem ShowMainWindowItem { get; }

        public NativeMenuItem HelpItem { get; }

        public void Attach(Window window)
        {
            if (window != null)
            {
                NativeMenu.SetMenu(window, SessionMenu);
            }
        }

        public void Update(MainWindowViewModel viewModel)
        {
            if (viewModel == null)
            {
                return;
            }

            bool session = viewModel.CanUseSessionActions;
            bool selected = viewModel.SelectedAccount != null;
            CheckUpdatesItem.IsEnabled = true;
            CopyCodeItem.IsEnabled = viewModel.CanCopyCode;
            LoginAgainItem.IsEnabled = session;
            ForceRefreshItem.IsEnabled = session;
            ViewConfirmationsItem.IsEnabled = selected;
            RemoveFromManifestItem.IsEnabled = selected;
            DeactivateItem.IsEnabled = selected;
            EncryptionItem.Header = viewModel.EncryptionMenuText + "...";
            EncryptionItem.IsEnabled = viewModel.CanManageEncryption;
        }

        private sealed class AlwaysEnabledCommand : ICommand
        {
            private readonly Action _execute;

            public AlwaysEnabledCommand(Action execute)
            {
                _execute = execute;
            }

            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }

            public bool CanExecute(object parameter)
            {
                return true;
            }

            public void Execute(object parameter)
            {
                if (_execute != null)
                {
                    _execute();
                }
            }
        }
    }
}
