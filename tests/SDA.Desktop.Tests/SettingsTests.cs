using SDA.Core.Storage;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using System;
using System.IO;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class SettingsTests : IDisposable
    {
        private readonly string _root;

        public SettingsTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-settings-").FullName;
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
        public void Load_PopulatesManifestValuesAndDoesNotWarnForExistingTrue()
        {
            string directory = WriteManifest(true, 12, true, true, true);
            SettingsWindowViewModel vm = new SettingsWindowViewModel(new ManifestSettingsService(), directory);

            vm.Load();

            Assert.True(vm.PeriodicChecking);
            Assert.Equal(12, vm.PeriodicCheckingInterval);
            Assert.True(vm.CheckAllAccounts);
            Assert.True(vm.AutoConfirmMarketTransactions);
            Assert.True(vm.AutoConfirmTrades);
            Assert.True(vm.DependentsEnabled);
            Assert.False(vm.NeedsAutoConfirmWarning(true, vm.AutoConfirmMarketTransactions));
            Assert.False(vm.NeedsAutoConfirmWarning(true, vm.AutoConfirmTrades));
        }

        [Fact]
        public void PeriodicCheckingFalse_DisablesDependents()
        {
            SettingsWindowViewModel vm = new SettingsWindowViewModel(new ManifestSettingsService(), WriteManifest(false, 8, true, true, true));
            vm.Load();

            Assert.False(vm.PeriodicChecking);
            Assert.False(vm.DependentsEnabled);
            Assert.True(vm.CheckAllAccounts);
            Assert.True(vm.AutoConfirmMarketTransactions);
        }

        [Fact]
        public void PeriodicCheckingTrue_EnablesDependents()
        {
            SettingsWindowViewModel vm = new SettingsWindowViewModel(new ManifestSettingsService(), WriteManifest(true, 8, false, false, false));
            vm.Load();

            Assert.True(vm.DependentsEnabled);
        }

        [Fact]
        public void Interval_ClampsBelowFiveWithoutRewritingUntilSave()
        {
            string directory = WriteManifest(true, 2, false, false, false);
            SettingsWindowViewModel vm = new SettingsWindowViewModel(new ManifestSettingsService(), directory);
            vm.Load();

            Assert.Equal(5, vm.PeriodicCheckingInterval);
            Assert.Equal(2, Manifest.GetManifest(directory).PeriodicCheckingInterval);

            vm.PeriodicCheckingInterval = 1;
            Assert.Equal(5, vm.PeriodicCheckingInterval);
        }

        [Fact]
        public void Save_WritesAllFiveFields()
        {
            string directory = WriteManifest(false, 5, false, false, false);
            SettingsWindowViewModel vm = new SettingsWindowViewModel(new ManifestSettingsService(), directory);
            vm.Load();
            vm.PeriodicChecking = true;
            vm.PeriodicCheckingInterval = 15;
            vm.CheckAllAccounts = true;
            vm.SetAutoConfirmMarketTransactions(true);
            vm.SetAutoConfirmTrades(true);

            SettingsSaveResult result = vm.Save();

            Assert.True(result.Succeeded);
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.PeriodicChecking);
            Assert.Equal(15, manifest.PeriodicCheckingInterval);
            Assert.True(manifest.CheckAllAccounts);
            Assert.True(manifest.AutoConfirmMarketTransactions);
            Assert.True(manifest.AutoConfirmTrades);
        }

        [Fact]
        public void SaveFailure_DoesNotReportSuccess()
        {
            SettingsWindowViewModel vm = new SettingsWindowViewModel(new FailingSettings(), WriteManifest(false, 5, false, false, false));
            vm.Load();
            vm.PeriodicChecking = true;

            SettingsSaveResult result = vm.Save();

            Assert.False(result.Succeeded);
            Assert.Equal(ManifestSettingsService.SaveFailedMessage, vm.StatusText);
        }

        [Fact]
        public void AutoConfirmMarket_FalseToTrueRequiresWarning()
        {
            SettingsWindowViewModel vm = new SettingsWindowViewModel(new ManifestSettingsService(), WriteManifest(true, 5, false, false, false));
            vm.Load();

            Assert.True(vm.NeedsAutoConfirmWarning(true, vm.AutoConfirmMarketTransactions));
            vm.SetAutoConfirmMarketTransactions(false);
            Assert.False(vm.AutoConfirmMarketTransactions);
        }

        [Fact]
        public void AutoConfirmTrades_FalseToTrueRequiresWarning()
        {
            SettingsWindowViewModel vm = new SettingsWindowViewModel(new ManifestSettingsService(), WriteManifest(true, 5, false, false, false));
            vm.Load();

            Assert.True(vm.NeedsAutoConfirmWarning(true, vm.AutoConfirmTrades));
        }

        [Fact]
        public void RuntimeClamp_UsesMinimumFive()
        {
            ManifestRuntimeSettings settings = new ManifestRuntimeSettings { PeriodicCheckingInterval = 3 };

            Assert.Equal(5, settings.EffectiveIntervalSeconds);
            Assert.Equal(5, ManifestRuntimeSettings.ClampInterval(0));
            Assert.Equal(5, ManifestRuntimeSettings.ClampInterval(4));
            Assert.Equal(9, ManifestRuntimeSettings.ClampInterval(9));
        }

        private string WriteManifest(bool checking, int interval, bool all, bool market, bool trades)
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Manifest manifest = Manifest.GenerateNewManifest(directory, false);
            manifest.PeriodicChecking = checking;
            manifest.PeriodicCheckingInterval = interval;
            manifest.CheckAllAccounts = all;
            manifest.AutoConfirmMarketTransactions = market;
            manifest.AutoConfirmTrades = trades;
            Assert.True(manifest.Save());
            return directory;
        }

        private sealed class FailingSettings : ManifestSettingsService
        {
            public override SettingsSaveResult Save(string directory, ManifestRuntimeSettings settings)
            {
                return new SettingsSaveResult(false, SaveFailedMessage);
            }
        }
    }
}
