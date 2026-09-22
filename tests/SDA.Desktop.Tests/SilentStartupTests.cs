using SDA.Core.Common;
using SDA.Core.Storage;
using SDA.Desktop;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class SilentStartupTests : IDisposable
    {
        private readonly string _root;

        public SilentStartupTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-silent-").FullName;
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
        public void SilentFlag_MeansHiddenToTray()
        {
            AppStartupOptions options = AppStartupOptions.FromCommandLine(new CommandLineOptions { Silent = true });

            Assert.True(options.StartHiddenToTray);
        }

        [Fact]
        public void Silent_HidesAfterInitialization()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, true);

            lifecycle.ApplyStartupVisibility();

            Assert.True(lifecycle.IsHiddenToTray);
            Assert.Equal(1, hosts.HideCount);
            Assert.Equal(0, hosts.ShowCount);
        }

        [Fact]
        public async Task SilentPlusValidKey_LoadsWithoutPromptAndStaysHidden()
        {
            string directory = WriteEncrypted("fixture-cli-pass");
            ScriptedPrompt prompt = new ScriptedPrompt();
            MainWindowViewModel vm = CreateViewModel(directory, prompt, "fixture-cli-pass");
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, true);

            await vm.LoadDirectoryAsync(directory, false);
            lifecycle.ApplyStartupVisibility();

            Assert.Equal(0, prompt.Calls);
            Assert.Equal("fixture_user", vm.SelectedAccount.DisplayName);
            Assert.True(lifecycle.IsHiddenToTray);
        }

        [Fact]
        public async Task SilentPlusInvalidKey_RequiresUnlockUi()
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
        public void SilentPlusInvalidKeyThenUnlock_ReturnsToHidden()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, true);
            lifecycle.ApplyStartupVisibility();

            lifecycle.BeginUnlockPrompt();
            Assert.Equal(1, hosts.ShowCount);
            lifecycle.EndUnlockPrompt();

            Assert.True(lifecycle.IsHiddenToTray);
            Assert.Equal(2, hosts.HideCount);
        }

        [Fact]
        public void Hidden_DoesNotCreateSecondServiceStop()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, true);
            lifecycle.ApplyStartupVisibility();
            lifecycle.ShowMainWindow();
            lifecycle.HideMainWindow();

            Assert.Equal(0, hosts.StopServicesCount);
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

        private sealed class RecordingHosts : IWindowVisibilityHost, IApplicationShutdownHost
        {
            public int ShowCount;
            public int HideCount;
            public int StopServicesCount;

            public void ShowNormal()
            {
                ShowCount++;
            }

            public void HideWindow()
            {
                HideCount++;
            }

            public void CloseMainWindow()
            {
            }

            public void StopBackgroundServices()
            {
                StopServicesCount++;
            }

            public void CloseSecondaryWindows()
            {
            }

            public void DisposeTrayIcon()
            {
            }

            public void Shutdown()
            {
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
