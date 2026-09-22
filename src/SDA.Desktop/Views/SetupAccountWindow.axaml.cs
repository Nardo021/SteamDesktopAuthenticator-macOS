using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Views
{
    public partial class SetupAccountWindow : Window
    {
        private readonly SetupAccountWindowViewModel _viewModel;
        private readonly ISteamLoginService _login;
        private CancellationTokenSource _loginCancellation;
        private int _dialogBusy;
        private bool _dialogResultSet;

        public SetupAccountWindow()
            : this(null, null, null, null, null)
        {
        }

        public SetupAccountWindow(
            ISteamLoginService login,
            IAuthenticatorLinkerFactory linkers,
            EnrollmentPersistenceService persistence,
            string directory,
            string currentPassKey)
        {
            InitializeComponent();
            _login = login ?? new SteamLoginService();
            _viewModel = new SetupAccountWindowViewModel(_login, linkers, persistence, directory, currentPassKey);
            DataContext = _viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        public SetupAccountWindowViewModel ViewModel
        {
            get { return _viewModel; }
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            CancelLogin();
            ClearSecrets();
            _viewModel.Close();
            base.OnClosed(e);
        }

        private async void OnLoginClick(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanLogin)
            {
                return;
            }

            string password = PasswordBox.Text;
            _loginCancellation = new CancellationTokenSource();
            LoginChallengeHandler challenges = new LoginChallengeHandler(
                new SteamGuardDeviceCodeProvider(null),
                RequestEmailCodeAsync,
                message => Dispatcher.UIThread.Post(() => _viewModel.Report(message)),
                _loginCancellation.Token);
            try
            {
                await _viewModel.LoginAsync(password, challenges);
            }
            finally
            {
                password = null;
                PasswordBox.Text = "";
            }
        }

        private async void OnContinueClick(object sender, RoutedEventArgs e)
        {
            await _viewModel.ConfirmContinueAsync();
        }

        private void OnDeclineContinueClick(object sender, RoutedEventArgs e)
        {
            _viewModel.DeclineContinue();
            CloseIfCancelledWithoutRecovery();
        }

        private async void OnEmailConfirmedClick(object sender, RoutedEventArgs e)
        {
            await _viewModel.ConfirmEmailAsync();
        }

        private async void OnNewPasskeyClick(object sender, RoutedEventArgs e)
        {
            string passKey = NewPasskeyBox.Text;
            await _viewModel.SubmitNewPasskeyAsync(passKey);
            NewPasskeyBox.Text = "";
        }

        private async void OnExistingPasskeyClick(object sender, RoutedEventArgs e)
        {
            string passKey = ExistingPasskeyBox.Text;
            await _viewModel.SubmitExistingPasskeyAsync(passKey);
            ExistingPasskeyBox.Text = "";
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.State == EnrollmentUiState.Authenticating)
            {
                CancelLogin();
                return;
            }

            _viewModel.Cancel();
            CloseIfCancelledWithoutRecovery();
        }

        private void OnCloseFinishedClick(object sender, RoutedEventArgs e)
        {
            CloseWithResult(_viewModel.State == EnrollmentUiState.Completed);
        }

        private async void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SetupAccountWindowViewModel.State) && !string.IsNullOrEmpty(e.PropertyName))
            {
                return;
            }

            await HandleDialogsAsync();
        }

        private async Task HandleDialogsAsync()
        {
            if (Interlocked.Exchange(ref _dialogBusy, 1) == 1)
            {
                return;
            }

            try
            {
                while (NeedsDialog(_viewModel.State))
                {
                    if (_viewModel.State == EnrollmentUiState.NeedPhoneNumber)
                    {
                        await PromptPhoneAsync();
                    }
                    else if (_viewModel.State == EnrollmentUiState.ShowRevocationCode)
                    {
                        await PromptRevocationDisplayAsync();
                    }
                    else if (_viewModel.State == EnrollmentUiState.ConfirmRevocationCode)
                    {
                        await PromptRevocationConfirmAsync();
                    }
                    else if (_viewModel.State == EnrollmentUiState.NeedSmsCode)
                    {
                        await PromptSmsAsync();
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _dialogBusy, 0);
            }
        }

        private static bool NeedsDialog(EnrollmentUiState state)
        {
            return state == EnrollmentUiState.NeedPhoneNumber
                || state == EnrollmentUiState.ShowRevocationCode
                || state == EnrollmentUiState.ConfirmRevocationCode
                || state == EnrollmentUiState.NeedSmsCode;
        }

        private async Task PromptPhoneAsync()
        {
            PhoneInputWindow window = new PhoneInputWindow();
            bool submitted = await window.ShowDialog<bool>(this);
            if (!submitted)
            {
                _viewModel.Cancel();
                CloseIfCancelledWithoutRecovery();
                return;
            }

            await _viewModel.SubmitPhoneAsync(window.PhoneNumber, window.CountryCode);
        }

        private async Task PromptRevocationDisplayAsync()
        {
            RevocationCodeWindow window = new RevocationCodeWindow(_viewModel.RevocationCode, false, null);
            bool continued = await window.ShowDialog<bool>(this);
            if (!continued)
            {
                _viewModel.Cancel();
                CloseIfCancelledWithoutRecovery();
                return;
            }

            _viewModel.ContinueRevocationDisplay();
        }

        private async Task PromptRevocationConfirmAsync()
        {
            RevocationCodeWindow window = new RevocationCodeWindow(_viewModel.RevocationCode, true, _viewModel.RevocationCode);
            bool submitted = await window.ShowDialog<bool>(this);
            if (!submitted)
            {
                _viewModel.Cancel();
                CloseIfCancelledWithoutRecovery();
                return;
            }

            await _viewModel.SubmitRevocationConfirmationAsync(window.EnteredCode);
        }

        private async Task PromptSmsAsync()
        {
            SmsCodeWindow window = new SmsCodeWindow(_viewModel.StatusText);
            bool submitted = await window.ShowDialog<bool>(this);
            if (!submitted)
            {
                _viewModel.Cancel();
                CloseIfCancelledWithoutRecovery();
                return;
            }

            string code = window.Code;
            window.Clear();
            await _viewModel.SubmitSmsAsync(code);
        }

        private Task<string> RequestEmailCodeAsync(string email, bool previousCodeWasIncorrect, CancellationToken cancellationToken)
        {
            TaskCompletionSource<string> done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    done.TrySetResult(null);
                    return;
                }

                EmailCodeWindow window = new EmailCodeWindow(previousCodeWasIncorrect);
                using (cancellationToken.Register(() => Dispatcher.UIThread.Post(() => window.Close(false))))
                {
                    bool submitted = await window.ShowDialog<bool>(this);
                    string code = submitted ? window.Code : null;
                    window.Clear();
                    done.TrySetResult(code);
                }
            });
            return done.Task;
        }

        private void CancelLogin()
        {
            CancellationTokenSource cancellation = _loginCancellation;
            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void ClearSecrets()
        {
            PasswordBox.Text = "";
            NewPasskeyBox.Text = "";
            ExistingPasskeyBox.Text = "";
        }

        private void CloseIfCancelledWithoutRecovery()
        {
            if (_viewModel.State == EnrollmentUiState.Cancelled && string.IsNullOrEmpty(_viewModel.RevocationCode))
            {
                CloseWithResult(false);
            }
        }

        private void CloseWithResult(bool completed)
        {
            if (_dialogResultSet)
            {
                return;
            }

            _dialogResultSet = true;
            Close(completed);
        }
    }
}
