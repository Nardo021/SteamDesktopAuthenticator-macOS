using Avalonia.Controls;
using Avalonia.Input;
using SDA.Desktop.Views;
using System.Linq;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class MainWindowMenuTests
    {
        [Fact]
        public void SessionMenu_UsesFileEditAccountWindowHelp()
        {
            MainWindowMenu menu = new MainWindowMenu();

            Assert.Equal(new[] { "File", "Edit", "Account", "Window", "Help" }, Headers(menu.SessionMenu));
            Assert.Equal(new[] { "Open maFiles Folder...", "Import Account..." }, Headers(menu.FileItem.Menu));
            Assert.Equal(new[] { "Copy Code" }, Headers(menu.EditItem.Menu));
            Assert.Equal(new[]
            {
                "Setup New Account...",
                "Login Again...",
                "Force Session Refresh",
                "View Confirmations",
                "Remove from Manifest...",
                "Deactivate Authenticator...",
                "Setup Encryption..."
            }, Headers(menu.AccountItem.Menu));
            Assert.Equal(new[] { "Steam Desktop Authenticator" }, Headers(menu.WindowItem.Menu));
            Assert.Equal(new[] { "Check for Updates..." }, Headers(menu.HelpItem.Menu));
            Assert.Equal(new KeyGesture(Key.O, KeyModifiers.Meta), menu.OpenFolderItem.Gesture);
            Assert.Equal(new KeyGesture(Key.I, KeyModifiers.Meta | KeyModifiers.Shift), menu.ImportAccountItem.Gesture);
        }

        [Fact]
        public void CheckForUpdates_IsAlwaysEnabled()
        {
            MainWindowMenu menu = new MainWindowMenu();
            bool invoked = false;
            menu.CheckUpdatesAction = () => invoked = true;

            Assert.True(menu.CheckUpdatesItem.IsEnabled);
            Assert.NotNull(menu.CheckUpdatesItem.Command);
            Assert.True(menu.CheckUpdatesItem.Command.CanExecute(null));
            menu.CheckUpdatesItem.Command.Execute(null);
            Assert.True(invoked);
        }

        private static string[] Headers(NativeMenu menu)
        {
            return menu.Items
                .OfType<NativeMenuItem>()
                .Select(item => item.Header)
                .Where(header => !string.IsNullOrEmpty(header) && header != "-")
                .ToArray();
        }
    }
}
