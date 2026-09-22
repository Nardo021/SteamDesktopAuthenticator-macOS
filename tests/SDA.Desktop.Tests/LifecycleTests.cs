using SDA.Desktop.Services;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class LifecycleTests
    {
        [Fact]
        public void NormalStartup_IsVisible()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, false);

            lifecycle.ApplyStartupVisibility();

            Assert.Equal(ApplicationVisibilityState.Visible, lifecycle.State);
            Assert.Equal(1, hosts.ShowCount);
            Assert.Equal(0, hosts.HideCount);
        }

        [Fact]
        public void SilentStartup_HidesToTray()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, true);

            lifecycle.ApplyStartupVisibility();

            Assert.Equal(ApplicationVisibilityState.HiddenToTray, lifecycle.State);
            Assert.True(lifecycle.IsHiddenToTray);
            Assert.Equal(1, hosts.HideCount);
        }

        [Fact]
        public void Minimize_HidesAndKeepsAppAlive()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, false);
            lifecycle.ApplyStartupVisibility();

            lifecycle.HandleMinimized();

            Assert.Equal(ApplicationVisibilityState.HiddenToTray, lifecycle.State);
            Assert.Equal(1, hosts.HideCount);
            Assert.Equal(0, hosts.StopServicesCount);
            Assert.Equal(0, hosts.ShutdownCount);
        }

        [Fact]
        public void Restore_ShowsSameHostAndSetsVisible()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, false);
            lifecycle.ApplyStartupVisibility();
            lifecycle.HandleMinimized();

            lifecycle.ShowMainWindow();

            Assert.Equal(ApplicationVisibilityState.Visible, lifecycle.State);
            Assert.Equal(2, hosts.ShowCount);
            Assert.True(hosts.LastShowWasNormal);
        }

        [Fact]
        public void RedClose_UsesQuitPath()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, false);

            lifecycle.HandleMainWindowClose();

            Assert.Equal(ApplicationVisibilityState.ShuttingDown, lifecycle.State);
            Assert.Equal(1, hosts.StopServicesCount);
            Assert.Equal(1, hosts.CloseSecondaryCount);
            Assert.Equal(1, hosts.DisposeTrayCount);
            Assert.Equal(1, hosts.CloseMainCount);
            Assert.Equal(1, hosts.ShutdownCount);
            Assert.Equal(1, lifecycle.ShutdownCleanupCount);
        }

        [Fact]
        public void TrayQuitAndLifetimeShutdown_CleanupOnce()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, false);

            lifecycle.Quit();
            lifecycle.Quit();
            lifecycle.HandleExternalShutdown();

            Assert.Equal(1, lifecycle.ShutdownCleanupCount);
            Assert.Equal(1, hosts.StopServicesCount);
            Assert.Equal(1, hosts.ShutdownCount);
        }

        [Fact]
        public void HideRestoreRepeatedly_DoesNotStopServices()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, false);
            lifecycle.ApplyStartupVisibility();

            lifecycle.HideMainWindow();
            lifecycle.ShowMainWindow();
            lifecycle.HideMainWindow();
            lifecycle.ShowMainWindow();

            Assert.Equal(0, hosts.StopServicesCount);
            Assert.Equal(0, hosts.DisposeTrayCount);
            Assert.Equal(0, hosts.ShutdownCount);
            Assert.Equal(ApplicationVisibilityState.Visible, lifecycle.State);
        }

        [Fact]
        public async Task HiddenCopy_UsesCurrentViewModelCode()
        {
            RecordingHosts hosts = new RecordingHosts();
            ApplicationLifecycleService lifecycle = new ApplicationLifecycleService(hosts, hosts, true);
            lifecycle.ApplyStartupVisibility();
            RecordingClipboard clipboard = new RecordingClipboard();
            clipboard.Text = "stale";
            await clipboard.SetTextAsync("PFRRH");

            Assert.True(lifecycle.IsHiddenToTray);
            Assert.Equal("PFRRH", clipboard.Text);
            Assert.Equal(0, hosts.StopServicesCount);
        }

        private sealed class RecordingHosts : IWindowVisibilityHost, IApplicationShutdownHost
        {
            public int ShowCount;
            public int HideCount;
            public int CloseMainCount;
            public int StopServicesCount;
            public int CloseSecondaryCount;
            public int DisposeTrayCount;
            public int ShutdownCount;
            public bool LastShowWasNormal;

            public void ShowNormal()
            {
                ShowCount++;
                LastShowWasNormal = true;
            }

            public void HideWindow()
            {
                HideCount++;
            }

            public void CloseMainWindow()
            {
                CloseMainCount++;
            }

            public void StopBackgroundServices()
            {
                StopServicesCount++;
            }

            public void CloseSecondaryWindows()
            {
                CloseSecondaryCount++;
            }

            public void DisposeTrayIcon()
            {
                DisposeTrayCount++;
            }

            public void Shutdown()
            {
                ShutdownCount++;
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
