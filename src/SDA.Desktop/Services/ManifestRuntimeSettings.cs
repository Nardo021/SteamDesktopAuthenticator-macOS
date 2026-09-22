using SDA.Core.Storage;
using System;

namespace SDA.Desktop.Services
{
    public sealed class ManifestRuntimeSettings
    {
        public const int MinimumIntervalSeconds = 5;

        // Avalonia NumericUpDown requires a maximum. 86400 seconds (1 day) is intentionally non-restrictive.
        public const int MaximumIntervalSeconds = 86400;

        public bool PeriodicChecking { get; set; }

        public int PeriodicCheckingInterval { get; set; }

        public bool CheckAllAccounts { get; set; }

        public bool AutoConfirmMarketTransactions { get; set; }

        public bool AutoConfirmTrades { get; set; }

        public int EffectiveIntervalSeconds
        {
            get { return ClampInterval(PeriodicCheckingInterval); }
        }

        public static int ClampInterval(int seconds)
        {
            if (seconds < MinimumIntervalSeconds)
            {
                return MinimumIntervalSeconds;
            }

            if (seconds > MaximumIntervalSeconds)
            {
                return MaximumIntervalSeconds;
            }

            return seconds;
        }

        public static ManifestRuntimeSettings FromManifest(Manifest manifest)
        {
            ManifestRuntimeSettings settings = new ManifestRuntimeSettings();
            if (manifest == null)
            {
                settings.PeriodicCheckingInterval = MinimumIntervalSeconds;
                return settings;
            }

            settings.PeriodicChecking = manifest.PeriodicChecking;
            settings.PeriodicCheckingInterval = manifest.PeriodicCheckingInterval;
            settings.CheckAllAccounts = manifest.CheckAllAccounts;
            settings.AutoConfirmMarketTransactions = manifest.AutoConfirmMarketTransactions;
            settings.AutoConfirmTrades = manifest.AutoConfirmTrades;
            return settings;
        }

        public ManifestRuntimeSettings Clone()
        {
            return new ManifestRuntimeSettings
            {
                PeriodicChecking = PeriodicChecking,
                PeriodicCheckingInterval = PeriodicCheckingInterval,
                CheckAllAccounts = CheckAllAccounts,
                AutoConfirmMarketTransactions = AutoConfirmMarketTransactions,
                AutoConfirmTrades = AutoConfirmTrades
            };
        }
    }

    public sealed class SettingsSaveResult
    {
        public SettingsSaveResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message ?? "";
        }

        public bool Succeeded { get; }

        public string Message { get; }
    }

    public class ManifestSettingsService
    {
        public const string SaveFailedMessage = "Unable to save settings.";

        public ManifestRuntimeSettings Read(string directory)
        {
            Manifest manifest = TryGet(directory);
            return ManifestRuntimeSettings.FromManifest(manifest);
        }

        public virtual SettingsSaveResult Save(string directory, ManifestRuntimeSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(directory))
            {
                return new SettingsSaveResult(false, SaveFailedMessage);
            }

            return ManifestMutationGate.Shared.Run(() => SaveUnlocked(directory, settings));
        }

        private SettingsSaveResult SaveUnlocked(string directory, ManifestRuntimeSettings settings)
        {
            Manifest manifest;
            try
            {
                manifest = Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return new SettingsSaveResult(false, SaveFailedMessage);
            }

            MaFilesDirectorySnapshot snapshot = MaFilesDirectorySnapshot.Capture(directory);
            bool previousChecking = manifest.PeriodicChecking;
            int previousInterval = manifest.PeriodicCheckingInterval;
            bool previousAll = manifest.CheckAllAccounts;
            bool previousMarket = manifest.AutoConfirmMarketTransactions;
            bool previousTrades = manifest.AutoConfirmTrades;

            manifest.PeriodicChecking = settings.PeriodicChecking;
            manifest.PeriodicCheckingInterval = ManifestRuntimeSettings.ClampInterval(settings.PeriodicCheckingInterval);
            manifest.CheckAllAccounts = settings.CheckAllAccounts;
            manifest.AutoConfirmMarketTransactions = settings.AutoConfirmMarketTransactions;
            manifest.AutoConfirmTrades = settings.AutoConfirmTrades;

            try
            {
                if (!manifest.Save())
                {
                    snapshot.Restore();
                    return new SettingsSaveResult(false, SaveFailedMessage);
                }
            }
            catch (Exception)
            {
                snapshot.Restore();
                manifest.PeriodicChecking = previousChecking;
                manifest.PeriodicCheckingInterval = previousInterval;
                manifest.CheckAllAccounts = previousAll;
                manifest.AutoConfirmMarketTransactions = previousMarket;
                manifest.AutoConfirmTrades = previousTrades;
                return new SettingsSaveResult(false, SaveFailedMessage);
            }

            return new SettingsSaveResult(true, "");
        }

        private static Manifest TryGet(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            try
            {
                return Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
