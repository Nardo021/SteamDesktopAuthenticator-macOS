using SDA.Core.Common;
using SDA.Core.Storage;
using SDA.Desktop;
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
    public class DesktopCliTests : IDisposable
    {
        private readonly string _root;

        public DesktopCliTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-cli-").FullName;
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
        public void StartupOptions_ParseEncryptionKeyAndSilent()
        {
            AppStartupOptions shortFlags = AppStartupOptions.FromCommandLine(CommandLineStartup.Parse(new[] { "-k", "fixture-cli-key", "-s" }));
            AppStartupOptions longFlags = AppStartupOptions.FromCommandLine(CommandLineStartup.Parse(new[] { "--encryption-key", "fixture-cli-key", "--silent" }));

            Assert.Equal("fixture-cli-key", shortFlags.EncryptionKey);
            Assert.True(shortFlags.StartMinimized);
            Assert.Equal("fixture-cli-key", longFlags.EncryptionKey);
            Assert.True(longFlags.StartMinimized);
        }

        [Fact]
        public async Task ValidCliPasskey_LoadsEncryptedAccountsWithoutPrompt()
        {
            string directory = WriteEncrypted("fixture-cli-pass");
            ScriptedPrompt prompt = new ScriptedPrompt();
            MainWindowViewModel vm = CreateViewModel(directory, prompt, "fixture-cli-pass");

            await vm.LoadDirectoryAsync(directory, false);

            Assert.Equal(0, prompt.Calls);
            Assert.Equal("fixture_user", vm.SelectedAccount.DisplayName);
            Assert.Equal("fixture-cli-pass", vm.CurrentPassKey);
        }

        [Fact]
        public async Task InvalidCliPasskey_FallsBackToUnlockPrompt()
        {
            string directory = WriteEncrypted("fixture-cli-pass");
            ScriptedPrompt prompt = new ScriptedPrompt();
            prompt.Passwords.Enqueue("fixture-cli-pass");
            MainWindowViewModel vm = CreateViewModel(directory, prompt, "wrong-cli-pass");

            await vm.LoadDirectoryAsync(directory, false);

            Assert.Equal(1, prompt.Calls);
            Assert.Equal("fixture_user", vm.SelectedAccount.DisplayName);
        }

        [Fact]
        public async Task CliKeyOnPlaintextManifest_DoesNotEnableEncryption()
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Manifest manifest = Manifest.GenerateNewManifest(directory, false);
            Assert.True(manifest.SaveAccount(Phase6Fixtures.LoadPlainFixture(), false));
            ScriptedPrompt prompt = new ScriptedPrompt();
            MainWindowViewModel vm = CreateViewModel(directory, prompt, "fixture-unused-key");

            await vm.LoadDirectoryAsync(directory, false);

            Assert.Equal(0, prompt.Calls);
            Assert.False(vm.ManifestEncrypted);
            Assert.Null(vm.CurrentPassKey);
            Assert.False(Manifest.GetManifest(directory).Encrypted);
        }

        [Fact]
        public void SilentFlag_SetsStartMinimized()
        {
            AppStartupOptions options = AppStartupOptions.FromCommandLine(new CommandLineOptions { Silent = true });

            Assert.True(options.StartMinimized);
        }

        private MainWindowViewModel CreateViewModel(string directory, ScriptedPrompt prompt, string cliKey)
        {
            return new MainWindowViewModel(
                new AccountService(),
                new SettingsService(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json")),
                new FixedClock(),
                prompt,
                new ScriptedFolders(),
                new RecordingClipboard(),
                directory,
                null,
                null,
                cliKey);
        }

        private string WriteEncrypted(string passKey)
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Phase6Fixtures.Destination(directory, true, passKey, Phase6Fixtures.LoadPlainFixture());
            return directory;
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

        private sealed class FixedClock : ISteamClock
        {
            public Task<long> GetSteamTimeAsync()
            {
                return Task.FromResult(1600000000L);
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
