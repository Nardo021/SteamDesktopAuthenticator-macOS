using Newtonsoft.Json;
using SDA.Core.Storage;
using SDA.Desktop.Models;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SDA.Platform.Mac;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class DesktopServiceTests : IDisposable
    {
        private static readonly object MacAppPathsLock = new object();
        private readonly string _root;

        public DesktopServiceTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-desktop-").FullName;
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
        public void MacAppPaths_UseApplicationDataDirectory()
        {
            lock (MacAppPathsLock)
            {
                Environment.SetEnvironmentVariable("SDA_DATA_DIRECTORY", null);
                string applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string support = MacAppPaths.GetApplicationSupportDirectory();
                string maFiles = MacAppPaths.GetDefaultMaFilesDirectory();
                string settings = MacAppPaths.GetSettingsFilePath();

                Assert.Equal(Path.Combine(applicationData, "Steam Desktop Authenticator"), support);
                Assert.Equal(Path.Combine(support, "maFiles"), maFiles);
                Assert.Equal(Path.Combine(support, "settings.json"), settings);
                Assert.DoesNotContain("Users" + Path.DirectorySeparatorChar + "username", support);
            }
        }

        [Fact]
        public void MacAppPaths_OverrideUsesEnvironmentVariable()
        {
            lock (MacAppPathsLock)
            {
                string expected = Path.Combine(_root, "override-support");
                Environment.SetEnvironmentVariable("SDA_DATA_DIRECTORY", expected);
                try
                {
                    Assert.Equal(expected, MacAppPaths.GetApplicationSupportDirectory());
                    Assert.Equal(Path.Combine(expected, "maFiles"), MacAppPaths.GetDefaultMaFilesDirectory());
                }
                finally
                {
                    Environment.SetEnvironmentVariable("SDA_DATA_DIRECTORY", null);
                }
            }
        }

        [Fact]
        public void EnsureDirectories_CreatesOnlyTheRequestedFolders()
        {
            string support = Path.Combine(_root, "support");
            string maFiles = Path.Combine(support, "maFiles");

            MacPlatformServices.EnsureDirectories(support, maFiles);

            Assert.True(Directory.Exists(support));
            Assert.True(Directory.Exists(maFiles));
            Assert.False(File.Exists(Path.Combine(maFiles, "manifest.json")));
        }

        [Fact]
        public void Settings_RoundTripStoresOnlyTheDirectory()
        {
            string path = Path.Combine(_root, "settings.json");
            SettingsService service = new SettingsService(path);
            service.Save(new AppSettings { MaFilesDirectory = Path.Combine(_root, "maFiles") });

            string json = File.ReadAllText(path);
            AppSettings loaded = service.Load();

            Assert.Equal(Path.Combine(_root, "maFiles"), loaded.MaFilesDirectory);
            Assert.Contains("\"maFilesDirectory\"", json);
            Assert.DoesNotContain("shared_secret", json);
            Assert.DoesNotContain("identity_secret", json);
            Assert.DoesNotContain("AccessToken", json);
            Assert.DoesNotContain("encryption", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Settings_CorruptFileFallsBackToEmpty()
        {
            string path = Path.Combine(_root, "settings.json");
            File.WriteAllText(path, "{");

            AppSettings loaded = new SettingsService(path).Load();

            Assert.Null(loaded.MaFilesDirectory);
        }

        [Fact]
        public void SteamGuardPeriod_MatchesThirtySecondWindows()
        {
            Assert.Equal(30, SteamGuardPeriod.SecondsRemaining(1599999990));
            Assert.Equal(20, SteamGuardPeriod.SecondsRemaining(1600000000));
            Assert.Equal(1, SteamGuardPeriod.SecondsRemaining(1600000019));
        }

        [Fact]
        public void SteamGuardDisplay_UsesSteamAuthForKnownTime()
        {
            SteamGuardAccount account = LoadPlainAccount();

            SteamGuardDisplayState state = SteamGuardDisplay.ForAccount(account, 1600000000);

            Assert.Equal("PFRRH", state.Code);
            Assert.True(state.CanCopy);
            Assert.Equal(20, state.SecondsRemaining);
        }

        [Fact]
        public void SteamGuardDisplay_MissingSecretDoesNotInventACode()
        {
            SteamGuardAccount account = LoadPlainAccount();
            account.SharedSecret = "";

            SteamGuardDisplayState state = SteamGuardDisplay.ForAccount(account, 1600000000);

            Assert.Equal("", state.Code);
            Assert.False(state.CanCopy);
            Assert.Equal("Account does not contain a valid authenticator", state.StatusText);
        }

        [Fact]
        public void AccountService_EmptyDirectoryDoesNotCreateManifest()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);

            MaFilesLoadResult result = new AccountService().Load(directory, null);

            Assert.Equal(MaFilesLoadKind.NoAccounts, result.Kind);
            Assert.Equal("No accounts found", result.StatusText);
            Assert.False(File.Exists(Path.Combine(directory, "manifest.json")));
        }

        [Fact]
        public void AccountService_InvalidManifestDoesNotThrow()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{ not json");

            MaFilesLoadResult result = new AccountService().Load(directory, null);

            Assert.Equal(MaFilesLoadKind.InvalidManifest, result.Kind);
            Assert.Equal("Invalid manifest", result.StatusText);
            Assert.False(result.RememberDirectory);
        }

        [Fact]
        public async Task Timer_TicksOnceAndStopsAfterDispose()
        {
            int ticks = 0;
            TimerService timer = new TimerService(TimeSpan.FromMilliseconds(40));
            timer.Tick += (_, __) => Interlocked.Increment(ref ticks);
            timer.Start(null);

            await Task.Delay(180);
            timer.Dispose();
            int afterDispose = ticks;
            await Task.Delay(120);

            Assert.True(afterDispose >= 1);
            Assert.Equal(afterDispose, ticks);
        }

        [Fact]
        public async Task ViewModel_LoadsPlainAccountAndCopiesExactCode()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.SaveAccount(LoadPlainAccount(), false));
            Harness harness = CreateHarness();

            await harness.ViewModel.LoadDirectoryAsync(directory, false);

            Assert.Equal("fixture_user", harness.ViewModel.SelectedAccount.DisplayName);
            Assert.Equal("PFRRH", harness.ViewModel.CurrentCode);
            Assert.Equal("expires in 20s", harness.ViewModel.CountdownText);
            Assert.True(harness.ViewModel.CanCopyCode);

            await harness.ViewModel.CopyAsync();

            Assert.Equal("PFRRH", harness.Clipboard.Text);
            Assert.Equal("Copied", harness.ViewModel.StatusText);
        }

        [Fact]
        public async Task ViewModel_SwitchingAccountsUpdatesCodeWithoutAnotherClockRead()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            SteamGuardAccount first = LoadPlainAccount();
            SteamGuardAccount second = LoadPlainAccount();
            second.AccountName = "fixture_two";
            second.SharedSecret = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("synthetic-shared-secret-2"));
            second.Session.SteamID = first.Session.SteamID + 1;
            Assert.True(manifest.SaveAccount(first, false));
            Assert.True(manifest.SaveAccount(second, false));
            string expectedSecond = second.GenerateSteamGuardCodeForTime(1600000000);
            Assert.NotEqual("PFRRH", expectedSecond);
            Harness harness = CreateHarness();

            await harness.ViewModel.LoadDirectoryAsync(directory, false);
            int readsAfterLoad = harness.Clock.Reads;
            AccountViewModel other = null;
            foreach (AccountViewModel account in harness.ViewModel.Accounts)
            {
                if (account.DisplayName == "fixture_two")
                {
                    other = account;
                }
            }

            harness.ViewModel.SelectedAccount = other;

            Assert.Equal(2, harness.ViewModel.Accounts.Count);
            Assert.Equal(expectedSecond, harness.ViewModel.CurrentCode);
            Assert.Equal(readsAfterLoad, harness.Clock.Reads);
        }

        [Fact]
        public async Task ViewModel_WrongPasswordRetriesAndCorrectPasswordLoads()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.SaveAccount(LoadPlainAccount(), true, "fixture-pass"));
            Harness harness = CreateHarness();
            harness.Prompt.Passwords.Enqueue("wrong-passkey");
            harness.Prompt.Passwords.Enqueue("fixture-pass");

            await harness.ViewModel.LoadDirectoryAsync(directory, false);

            Assert.Equal(2, harness.Prompt.Calls);
            Assert.Equal("fixture_user", harness.ViewModel.SelectedAccount.DisplayName);
            Assert.Equal("PFRRH", harness.ViewModel.CurrentCode);
        }

        [Fact]
        public async Task ViewModel_CancelUnlockShowsDecryptError()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.SaveAccount(LoadPlainAccount(), true, "fixture-pass"));
            Harness harness = CreateHarness();

            await harness.ViewModel.LoadDirectoryAsync(directory, false);

            Assert.Equal("Unable to decrypt accounts", harness.ViewModel.StatusText);
            Assert.Null(harness.ViewModel.SelectedAccount);
            Assert.False(harness.ViewModel.CanCopyCode);
            Assert.Equal("—", harness.ViewModel.CurrentCode);
        }

        [Fact]
        public async Task ViewModel_RemembersSelectedFolderAndRestoresIt()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.SaveAccount(LoadPlainAccount(), false));
            string settingsPath = Path.Combine(_root, "settings.json");
            string untouchedDefault = Path.Combine(_root, "default-maFiles");
            Directory.CreateDirectory(untouchedDefault);
            Harness harness = CreateHarness(settingsPath, untouchedDefault);
            harness.Folders.Path = directory;

            await harness.ViewModel.OpenFolderAsync();
            string saved = File.ReadAllText(settingsPath);

            Harness restarted = CreateHarness(settingsPath, untouchedDefault);
            await restarted.ViewModel.InitializeAsync();

            Assert.Contains("maFilesDirectory", saved);
            Assert.DoesNotContain("shared_secret", saved);
            Assert.DoesNotContain("fixture-pass", saved);
            Assert.Equal("fixture_user", restarted.ViewModel.SelectedAccount.DisplayName);
            Assert.False(File.Exists(Path.Combine(untouchedDefault, "manifest.json")));
        }

        [Fact]
        public async Task ViewModel_InvalidManifestShowsError()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{");
            Harness harness = CreateHarness();

            await harness.ViewModel.LoadDirectoryAsync(directory, false);

            Assert.Equal("Invalid manifest", harness.ViewModel.StatusText);
            Assert.False(harness.ViewModel.CanCopyCode);
        }

        private Harness CreateHarness()
        {
            return CreateHarness(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json"), NewDirectory());
        }

        private Harness CreateHarness(string settingsPath, string defaultDirectory)
        {
            FixedClock clock = new FixedClock();
            ScriptedPrompt prompt = new ScriptedPrompt();
            ScriptedFolders folders = new ScriptedFolders();
            RecordingClipboard clipboard = new RecordingClipboard();
            MainWindowViewModel viewModel = new MainWindowViewModel(
                new AccountService(),
                new SettingsService(settingsPath),
                clock,
                prompt,
                folders,
                clipboard,
                defaultDirectory);
            return new Harness
            {
                ViewModel = viewModel,
                Clock = clock,
                Prompt = prompt,
                Folders = folders,
                Clipboard = clipboard
            };
        }

        private static SteamGuardAccount LoadPlainAccount()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fixture-plain-mafile.json");
            return JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(fixture));
        }

        private string NewDirectory()
        {
            return Path.Combine(_root, Guid.NewGuid().ToString("N"));
        }

        private sealed class Harness
        {
            public MainWindowViewModel ViewModel { get; set; }
            public FixedClock Clock { get; set; }
            public ScriptedPrompt Prompt { get; set; }
            public ScriptedFolders Folders { get; set; }
            public RecordingClipboard Clipboard { get; set; }
        }

        private sealed class FixedClock : ISteamClock
        {
            public int Reads;

            public Task<long> GetSteamTimeAsync()
            {
                Reads++;
                return Task.FromResult(1600000000L);
            }
        }

        private sealed class ScriptedPrompt : IEncryptionPrompt
        {
            public Queue<string> Passwords = new Queue<string>();
            public int Calls;

            public Task<string> PromptAsync(string errorMessage)
            {
                Calls++;
                if (Passwords.Count == 0)
                {
                    return Task.FromResult<string>(null);
                }

                return Task.FromResult(Passwords.Dequeue());
            }
        }

        private sealed class ScriptedFolders : IFolderPicker
        {
            public string Path;

            public Task<string> PickAsync()
            {
                return Task.FromResult(Path);
            }
        }

        private sealed class RecordingClipboard : IClipboardService
        {
            public string Text;

            public Task SetTextAsync(string text)
            {
                Text = text;
                return Task.CompletedTask;
            }
        }
    }
}
