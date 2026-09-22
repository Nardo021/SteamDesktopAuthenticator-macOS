using SDA.Desktop.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SDA.Desktop.ViewModels
{
    public sealed class EncryptionManagementViewModel : INotifyPropertyChanged
    {
        private string _statusText = "";
        private bool _busy;

        public EncryptionManagementViewModel(EncryptionManagementKind kind)
        {
            Kind = kind;
            IsManage = kind == EncryptionManagementKind.Manage;
            Title = IsManage ? "Manage Encryption" : "Setup Encryption";
            NewKeyHint = IsManage
                ? "Leave the new passkey blank to remove encryption."
                : "Leave blank to remain unencrypted.";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public EncryptionManagementKind Kind { get; }

        public bool IsManage { get; }

        public string Title { get; }

        public string NewKeyHint { get; }

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
                _busy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSubmit));
            }
        }

        public bool CanSubmit
        {
            get { return !Busy; }
        }

        public string ActivePassKey { get; set; }

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
