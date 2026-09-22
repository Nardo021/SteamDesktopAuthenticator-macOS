using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SDA.Desktop.ViewModels
{
    public enum LoginUiState
    {
        Idle,
        LoggingIn,
        Success,
        Failure,
        Cancelled
    }

    public sealed class LoginWindowViewModel : INotifyPropertyChanged
    {
        private LoginUiState _state = LoginUiState.Idle;
        private string _statusText = "";

        public LoginWindowViewModel(string accountName)
            : this(accountName, false)
        {
        }

        public LoginWindowViewModel(string accountName, bool import)
        {
            AccountName = accountName ?? "";
            Title = import ? "Import Account" : "Login Again";
            SessionHint = import
                ? "Sign in so this imported account can be saved with a current Steam session."
                : "Your Steam session will be renewed.";
        }

        public string Title { get; }

        public string SessionHint { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        public string AccountName { get; }

        public LoginUiState State
        {
            get { return _state; }
            private set
            {
                if (_state == value)
                {
                    return;
                }

                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LoginButtonText));
                OnPropertyChanged(nameof(CanSubmit));
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set
            {
                if (_statusText == value)
                {
                    return;
                }

                _statusText = value ?? "";
                OnPropertyChanged();
            }
        }

        public string LoginButtonText
        {
            get { return State == LoginUiState.LoggingIn ? "Logging in..." : "Login"; }
        }

        public bool CanSubmit
        {
            get { return State == LoginUiState.Idle || State == LoginUiState.Failure; }
        }

        public bool CanCancel
        {
            get { return true; }
        }

        public void MarkLoggingIn()
        {
            State = LoginUiState.LoggingIn;
            StatusText = "Logging in...";
        }

        public void SetStatus(string status)
        {
            StatusText = status ?? "";
        }

        public void MarkSucceeded()
        {
            State = LoginUiState.Success;
            StatusText = "Login successful.";
        }

        public void MarkFailed(string status)
        {
            State = LoginUiState.Failure;
            StatusText = string.IsNullOrEmpty(status) ? "Steam login failed." : status;
        }

        public void MarkCancelled()
        {
            State = LoginUiState.Cancelled;
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
