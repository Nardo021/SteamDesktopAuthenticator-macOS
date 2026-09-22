using Newtonsoft.Json;
using SDA.Core.Storage;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class TrayMenuTests : IDisposable
    {
        private readonly string _root;

        public TrayMenuTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-tray-").FullName;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public async Task ZeroAccounts_DisablesActionsAndShowsPlaceholder()
        {
            RecordingActions actions = new RecordingActions();
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            MainWindowViewModel main = await LoadAccountsAsync();
            main.SearchText = "missing";
            tray.Bind(main);

            Assert.False(tray.HasAccounts);
            Assert.False(tray.CanCopy);
            Assert.False(tray.CanViewConfirmations);
            tray.ViewConfirmations();
            await tray.CopySteamGuardAsync();
            Assert.Equal(0, actions.CopyCalls);
            Assert.Equal(0, actions.ConfirmationsCalls);
        }

        [Fact]
        public async Task OneAccount_IsPresentAndSelected()
        {
            RecordingActions actions = new RecordingActions();
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            MainWindowViewModel main = await LoadAccountsAsync(LoadPlain("only"));
            tray.Bind(main);

            Assert.True(tray.HasAccounts);
            Assert.Single(tray.Accounts);
            Assert.Equal("only", tray.Accounts[0].DisplayName);
            Assert.True(tray.Accounts[0].IsSelected);
            Assert.True(tray.CanViewConfirmations);
        }

        [Fact]
        public async Task MultipleAccounts_PreserveManifestOrder()
        {
            RecordingActions actions = new RecordingActions();
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            MainWindowViewModel main = await LoadAccountsAsync(LoadPlain("zebra"), LoadPlain("alpha", 1));
            tray.Bind(main);

            Assert.Equal(2, tray.Accounts.Count);
            Assert.Equal("zebra", tray.Accounts[0].DisplayName);
            Assert.Equal("alpha", tray.Accounts[1].DisplayName);
            Assert.True(tray.Accounts[0].IsSelected);
        }

        [Fact]
        public async Task TraySelection_ChangesMainWindowSelection()
        {
            RecordingActions actions = new RecordingActions();
            MainWindowViewModel main = await LoadAccountsAsync(LoadPlain("zebra"), LoadPlain("alpha", 1));
            actions.OnSelect = account => { main.SelectedAccount = account; };
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            tray.Bind(main);

            tray.SelectAccount(tray.Accounts[1].Account);
            tray.Refresh();

            Assert.Equal("alpha", main.SelectedAccount.DisplayName);
            Assert.True(tray.Accounts[1].IsSelected);
            Assert.False(tray.Accounts[0].IsSelected);
        }

        [Fact]
        public async Task MainWindowSelection_UpdatesTrayCheck()
        {
            RecordingActions actions = new RecordingActions();
            MainWindowViewModel main = await LoadAccountsAsync(LoadPlain("zebra"), LoadPlain("alpha", 1));
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            tray.Bind(main);

            main.SelectedAccount = main.AllAccounts[1];

            Assert.True(tray.Accounts[1].IsSelected);
            Assert.Equal("alpha", tray.Accounts[1].DisplayName);
        }

        [Fact]
        public async Task FilterDoesNotHideTrayAccounts()
        {
            RecordingActions actions = new RecordingActions();
            MainWindowViewModel main = await LoadAccountsAsync(LoadPlain("zebra"), LoadPlain("alpha", 1));
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            tray.Bind(main);

            main.SearchText = "alpha";

            Assert.Single(main.Accounts);
            Assert.Equal(2, tray.Accounts.Count);
        }

        [Fact]
        public async Task Reload_UpdatesTrayMenu()
        {
            RecordingActions actions = new RecordingActions();
            MainWindowViewModel main = await LoadAccountsAsync(LoadPlain("first"));
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            tray.Bind(main);
            Manifest manifest = Manifest.GetManifest(main.CurrentDirectory);
            Assert.True(manifest.SaveAccount(LoadPlain("second", 2), false));

            await main.ReloadPreservingSelectionAsync(null, 0);

            Assert.Equal(2, tray.Accounts.Count);
            Assert.Equal("first", tray.Accounts[0].DisplayName);
            Assert.Equal("second", tray.Accounts[1].DisplayName);
        }

        [Fact]
        public async Task Removal_UpdatesTrayMenu()
        {
            RecordingActions actions = new RecordingActions();
            MainWindowViewModel main = await LoadAccountsAsync(LoadPlain("keep"), LoadPlain("drop", 3));
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            tray.Bind(main);
            new ManifestAccountRemovalService().RemoveFromManifest(main.AllAccounts[1].Account, main.CurrentDirectory);

            await main.ReloadPreservingSelectionAsync(null, 0);

            Assert.Single(tray.Accounts);
            Assert.Equal("keep", tray.Accounts[0].DisplayName);
        }

        [Fact]
        public async Task Copy_UsesCurrentCodeAndQuitCallsLifecycle()
        {
            RecordingActions actions = new RecordingActions();
            MainWindowViewModel main = await LoadAccountsAsync(LoadPlain("copy"));
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            tray.Bind(main);

            Assert.True(tray.CanCopy);
            await tray.CopySteamGuardAsync();
            tray.ViewConfirmations();
            tray.Restore();
            tray.Quit();

            Assert.Equal(1, actions.CopyCalls);
            Assert.Equal(1, actions.ConfirmationsCalls);
            Assert.Equal(1, actions.RestoreCalls);
            Assert.Equal(1, actions.QuitCalls);
        }

        [Fact]
        public void BindTwice_DoesNotDuplicateSubscriptions()
        {
            RecordingActions actions = new RecordingActions();
            TrayMenuViewModel tray = new TrayMenuViewModel(actions);
            int changes = 0;
            tray.Changed += (_, __) => changes++;
            MainWindowViewModel main = CreateEmptyViewModel();
            tray.Bind(main);
            tray.Bind(main);
            main.SearchText = "x";
            main.SearchText = "";

            Assert.True(changes >= 1);
        }

        private async Task<MainWindowViewModel> LoadAccountsAsync(params SteamGuardAccount[] accounts)
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Manifest manifest = Manifest.GenerateNewManifest(directory, false);
            foreach (SteamGuardAccount account in accounts)
            {
                Assert.True(manifest.SaveAccount(account, false));
            }

            MainWindowViewModel vm = CreateEmptyViewModel(directory);
            await vm.LoadDirectoryAsync(directory, false);
            return vm;
        }

        private MainWindowViewModel CreateEmptyViewModel(string directory = null)
        {
            string path = directory ?? Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new MainWindowViewModel(
                new AccountService(),
                new SettingsService(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json")),
                new FixedClock(),
                new ScriptedPrompt(),
                new ScriptedFolders(),
                new RecordingClipboard(),
                path);
        }

        private static SteamGuardAccount LoadPlain(string name, int offset = 0)
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fixture-plain-mafile.json");
            SteamGuardAccount account = JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(fixture));
            account.AccountName = name;
            if (account.Session != null)
            {
                account.Session.SteamID += (ulong)offset;
            }

            return account;
        }

        private sealed class RecordingActions : ITrayMenuActions
        {
            public int RestoreCalls;
            public int CopyCalls;
            public int ConfirmationsCalls;
            public int QuitCalls;
            public Action<AccountViewModel> OnSelect;

            public void Restore()
            {
                RestoreCalls++;
            }

            public void SelectAccount(AccountViewModel account)
            {
                if (OnSelect != null)
                {
                    OnSelect(account);
                }
            }

            public Task CopySteamGuardAsync()
            {
                CopyCalls++;
                return Task.CompletedTask;
            }

            public void ViewConfirmations()
            {
                ConfirmationsCalls++;
            }

            public void Quit()
            {
                QuitCalls++;
            }
        }

        private sealed class FixedClock : ISteamClock
        {
            public Task<long> GetSteamTimeAsync()
            {
                return Task.FromResult(1600000000L);
            }
        }

        private sealed class ScriptedPrompt : IEncryptionPrompt
        {
            public Task<string> PromptAsync(string errorMessage)
            {
                return Task.FromResult<string>(null);
            }
        }

        private sealed class ScriptedFolders : IFolderPicker
        {
            public Task<string> PickAsync()
            {
                return Task.FromResult<string>(null);
            }
        }

        private sealed class RecordingClipboard : IClipboardService
        {
            public Task SetTextAsync(string text)
            {
                return Task.CompletedTask;
            }
        }
    }
}
