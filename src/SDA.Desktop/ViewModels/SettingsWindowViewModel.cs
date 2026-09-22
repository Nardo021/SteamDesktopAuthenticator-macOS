using SDA.Desktop.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SDA.Desktop.ViewModels
{
    public sealed class SettingsWindowViewModel : INotifyPropertyChanged
    {
        public const string AutoConfirmWarning =
            "Warning: enabling this will severely reduce the security of your items! Use of this option is at your own risk. Would you like to continue?";

        private readonly ManifestSettingsService _settings;
        private readonly string _directory;
        private bool _periodicChecking;
        private int _interval;
        private bool _checkAll;
        private bool _autoMarket;
        private bool _autoTrades;
        private string _statusText = "";
        private bool _loaded;

        public SettingsWindowViewModel(ManifestSettingsService settings, string directory)
        {
            _settings = settings ?? new ManifestSettingsService();
            _directory = directory;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool PeriodicChecking
        {
            get { return _periodicChecking; }
            set
            {
                if (_periodicChecking == value)
                {
                    return;
                }

                _periodicChecking = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DependentsEnabled));
            }
        }

        public int PeriodicCheckingInterval
        {
            get { return _interval; }
            set
            {
                int clamped = ManifestRuntimeSettings.ClampInterval(value);
                if (_interval == clamped)
                {
                    return;
                }

                _interval = clamped;
                OnPropertyChanged();
            }
        }

        public bool CheckAllAccounts
        {
            get { return _checkAll; }
            set
            {
                if (_checkAll == value)
                {
                    return;
                }

                _checkAll = value;
                OnPropertyChanged();
            }
        }

        public bool AutoConfirmMarketTransactions
        {
            get { return _autoMarket; }
            private set
            {
                if (_autoMarket == value)
                {
                    return;
                }

                _autoMarket = value;
                OnPropertyChanged();
            }
        }

        public bool AutoConfirmTrades
        {
            get { return _autoTrades; }
            private set
            {
                if (_autoTrades == value)
                {
                    return;
                }

                _autoTrades = value;
                OnPropertyChanged();
            }
        }

        public bool DependentsEnabled
        {
            get { return PeriodicChecking; }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set
            {
                _statusText = value ?? "";
                OnPropertyChanged();
            }
        }

        public void Load()
        {
            ManifestRuntimeSettings current = _settings.Read(_directory);
            _loaded = false;
            PeriodicChecking = current.PeriodicChecking;
            PeriodicCheckingInterval = ManifestRuntimeSettings.ClampInterval(current.PeriodicCheckingInterval);
            CheckAllAccounts = current.CheckAllAccounts;
            AutoConfirmMarketTransactions = current.AutoConfirmMarketTransactions;
            AutoConfirmTrades = current.AutoConfirmTrades;
            _loaded = true;
        }

        public bool NeedsAutoConfirmWarning(bool enabling, bool alreadyEnabled)
        {
            return _loaded && enabling && !alreadyEnabled;
        }

        public void SetAutoConfirmMarketTransactions(bool value)
        {
            AutoConfirmMarketTransactions = value;
        }

        public void SetAutoConfirmTrades(bool value)
        {
            AutoConfirmTrades = value;
        }

        public SettingsSaveResult Save()
        {
            ManifestRuntimeSettings settings = new ManifestRuntimeSettings
            {
                PeriodicChecking = PeriodicChecking,
                PeriodicCheckingInterval = PeriodicCheckingInterval,
                CheckAllAccounts = CheckAllAccounts,
                AutoConfirmMarketTransactions = AutoConfirmMarketTransactions,
                AutoConfirmTrades = AutoConfirmTrades
            };

            SettingsSaveResult result = _settings.Save(_directory, settings);
            if (!result.Succeeded)
            {
                StatusText = result.Message;
            }

            return result;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
