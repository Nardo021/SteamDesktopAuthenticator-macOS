using Newtonsoft.Json;
using SDA.Core.Storage;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class AccountFilterReorderTests : IDisposable
    {
        private readonly string _root;

        public AccountFilterReorderTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-filter-").FullName;
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
        public void EmptySearch_MatchesAll()
        {
            Assert.True(AccountFilter.Matches("Main", ""));
            Assert.True(AccountFilter.Matches("Main", null));
        }

        [Fact]
        public void SubstringSearch_IsCaseInsensitive()
        {
            Assert.True(AccountFilter.Matches("Fixture_User", "user"));
            Assert.True(AccountFilter.Matches("Fixture_User", "FIXTURE"));
            Assert.False(AccountFilter.Matches("Fixture_User", "other"));
        }

        [Fact]
        public void RegexPrefix_UsesTextAfterTilde()
        {
            Assert.True(AccountFilter.Matches("main_one", "~^main_"));
            Assert.False(AccountFilter.Matches("alt_one", "~^main_"));
        }

        [Fact]
        public void InvalidRegex_DoesNotThrowAndShowsAll()
        {
            Assert.True(AccountFilter.Matches("any", "~("));
        }

        [Fact]
        public async Task ViewModel_EmptySearchShowsManifestOrder()
        {
            Harness harness = await LoadTwoAccountsAsync();

            Assert.Equal(2, harness.ViewModel.Accounts.Count);
            Assert.Equal("zebra", harness.ViewModel.Accounts[0].DisplayName);
            Assert.Equal("alpha", harness.ViewModel.Accounts[1].DisplayName);
        }

        [Fact]
        public async Task ViewModel_ClearSearchRestoresManifestOrder()
        {
            Harness harness = await LoadTwoAccountsAsync();
            harness.ViewModel.SearchText = "alpha";
            Assert.Single(harness.ViewModel.Accounts);

            harness.ViewModel.SearchText = "";

            Assert.Equal("zebra", harness.ViewModel.Accounts[0].DisplayName);
            Assert.Equal("alpha", harness.ViewModel.Accounts[1].DisplayName);
        }

        [Fact]
        public async Task ViewModel_PreservesSelectionWhenStillVisible()
        {
            Harness harness = await LoadTwoAccountsAsync();
            harness.ViewModel.SelectedAccount = harness.ViewModel.Accounts[1];

            harness.ViewModel.SearchText = "a";

            Assert.Equal("alpha", harness.ViewModel.SelectedAccount.DisplayName);
        }

        [Fact]
        public async Task ViewModel_FilteredOutSelectionMovesToFirstVisible()
        {
            Harness harness = await LoadTwoAccountsAsync();
            harness.ViewModel.SelectedAccount = harness.ViewModel.Accounts[0];

            harness.ViewModel.SearchText = "alpha";

            Assert.Equal("alpha", harness.ViewModel.SelectedAccount.DisplayName);
        }

        [Fact]
        public async Task ViewModel_NoVisibleAccountsClearsSelection()
        {
            Harness harness = await LoadTwoAccountsAsync();

            harness.ViewModel.SearchText = "missing";

            Assert.Empty(harness.ViewModel.Accounts);
            Assert.Null(harness.ViewModel.SelectedAccount);
        }

        [Fact]
        public async Task MoveDown_PersistsManifestOrderAndSelection()
        {
            Harness harness = await LoadTwoAccountsAsync();
            Assert.Equal("zebra", harness.ViewModel.SelectedAccount.DisplayName);

            Assert.True(await harness.ViewModel.TryMoveSelectedAsync(AccountMoveDirection.Down));

            Assert.Equal("alpha", harness.ViewModel.Accounts[0].DisplayName);
            Assert.Equal("zebra", harness.ViewModel.Accounts[1].DisplayName);
            Assert.Equal("zebra", harness.ViewModel.SelectedAccount.DisplayName);
            Manifest manifest = Manifest.GetManifest(harness.Directory);
            Assert.Equal(harness.ViewModel.Accounts[0].SteamId, manifest.Entries[0].SteamID);
            Assert.Equal(harness.ViewModel.Accounts[1].SteamId, manifest.Entries[1].SteamID);
        }

        [Fact]
        public async Task MoveUp_PersistsManifestOrder()
        {
            Harness harness = await LoadTwoAccountsAsync();
            harness.ViewModel.SelectedAccount = harness.ViewModel.Accounts[1];

            Assert.True(await harness.ViewModel.TryMoveSelectedAsync(AccountMoveDirection.Up));

            Assert.Equal("alpha", harness.ViewModel.Accounts[0].DisplayName);
            Assert.Equal("zebra", harness.ViewModel.Accounts[1].DisplayName);
        }

        [Fact]
        public async Task FirstCannotMoveUp_LastCannotMoveDown()
        {
            Harness harness = await LoadTwoAccountsAsync();

            Assert.False(await harness.ViewModel.TryMoveSelectedAsync(AccountMoveDirection.Up));
            harness.ViewModel.SelectedAccount = harness.ViewModel.Accounts[1];
            Assert.False(await harness.ViewModel.TryMoveSelectedAsync(AccountMoveDirection.Down));
            Assert.Equal("zebra", harness.ViewModel.Accounts[0].DisplayName);
        }

        [Fact]
        public async Task FilteredList_BlocksReorder()
        {
            Harness harness = await LoadTwoAccountsAsync();
            harness.ViewModel.SearchText = "zebra";

            Assert.True(harness.ViewModel.IsFilterActive);
            Assert.False(await harness.ViewModel.TryMoveSelectedAsync(AccountMoveDirection.Down));
        }

        [Fact]
        public void ControlAndCommandShortcuts_ResolveMoveDirection()
        {
            AccountMoveDirection direction;
            Assert.True(AccountReorderShortcuts.TryResolve(true, false, true, false, out direction));
            Assert.Equal(AccountMoveDirection.Up, direction);
            Assert.True(AccountReorderShortcuts.TryResolve(false, true, false, true, out direction));
            Assert.Equal(AccountMoveDirection.Down, direction);
            Assert.False(AccountReorderShortcuts.TryResolve(false, false, true, false, out direction));
        }

        private async Task<Harness> LoadTwoAccountsAsync()
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Manifest manifest = Manifest.GenerateNewManifest(directory, false);
            SteamGuardAccount zebra = LoadPlainAccount();
            zebra.AccountName = "zebra";
            SteamGuardAccount alpha = LoadPlainAccount();
            alpha.AccountName = "alpha";
            alpha.Session.SteamID = zebra.Session.SteamID + 1;
            Assert.True(manifest.SaveAccount(zebra, false));
            Assert.True(manifest.SaveAccount(alpha, false));

            MainWindowViewModel vm = new MainWindowViewModel(
                new AccountService(),
                new SettingsService(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json")),
                new FixedClock(),
                new ScriptedPrompt(),
                new ScriptedFolders(),
                new RecordingClipboard(),
                directory);
            await vm.LoadDirectoryAsync(directory, false);
            return new Harness { ViewModel = vm, Directory = directory };
        }

        private static SteamGuardAccount LoadPlainAccount()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fixture-plain-mafile.json");
            return JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(fixture));
        }

        private sealed class Harness
        {
            public MainWindowViewModel ViewModel { get; set; }

            public string Directory { get; set; }
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
