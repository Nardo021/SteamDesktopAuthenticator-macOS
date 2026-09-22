using SDA.Desktop.Services;
using SteamAuth;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SDA.Desktop.ViewModels
{
    public sealed class ImportAccountWindowViewModel : INotifyPropertyChanged
    {
        private string _sourcePath = "";
        private string _statusText = "";
        private bool _busy;

        public event PropertyChangedEventHandler PropertyChanged;

        public string SourcePath
        {
            get { return _sourcePath; }
            set
            {
                if (_sourcePath == value)
                {
                    return;
                }

                _sourcePath = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanImport));
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            set
            {
                _statusText = value ?? "";
                OnPropertyChanged();
            }
        }

        public bool Busy
        {
            get { return _busy; }
            set
            {
                if (_busy == value)
                {
                    return;
                }

                _busy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanImport));
            }
        }

        public bool CanImport
        {
            get { return !_busy && !string.IsNullOrEmpty(SourcePath); }
        }

        public SteamGuardAccount ImportedAccount { get; set; }

        public string SuccessMessage { get; set; }

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
