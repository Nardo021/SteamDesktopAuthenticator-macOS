using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Views
{
    public partial class LoginWindow : Window, ILoginStatus
    {
        private readonly SteamGuardAccount _account;
        private readonly ISteamLoginService _login;
        private readonly Func<SessionData, IEncryptionPrompt, Task<string>> _commit;
        private readonly LoginWindowViewModel _viewModel;
        private readonly bool _import;
        private CancellationTokenSource _cancellation;
        private int _loginStarted;

        public LoginWindow()
            : this(null, null, null)
        {
        }

        public LoginWindow(
            SteamGuardAccount account,
            ISteamLoginService login,
            Func<SessionData, IEncryptionPrompt, Task<string>> commit)
            : this(account, login, commit, false)
        {
        }

        public static LoginWindow ForImport(SteamGuardAccount account, ISteamLoginService login)
        {
            return new LoginWindow(account, login, null, true);
        }

        private LoginWindow(
            SteamGuardAccount account,
            ISteamLoginService login,
            Func<SessionData, IEncryptionPrompt, Task<string>> commit,
            bool import)
        {
            InitializeComponent();
            _account = account;
            _login = login;
            _commit = commit;
            _import = import;
            _viewModel = new LoginWindowViewModel(account == null ? "" : account.AccountName, import);
            DataContext = _viewModel;
            Title = _viewModel.Title;
        }

        public SessionData ResultSession { get; private set; }

        public void Report(string status)
        {
            Dispatcher.UIThread.Post(() => _viewModel.SetStatus(status));
        }

        protected override void OnClosed(EventArgs e)
        {
            CancelLogin();
            PasswordBox.Text = "";
            base.OnClosed(e);
        }

        private async void OnLoginClick(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanSubmit || _account == null || _login == null)
            {
                return;
            }

            if (Interlocked.Exchange(ref _loginStarted, 1) == 1)
            {
                return;
            }

            string password = PasswordBox.Text;
            _viewModel.MarkLoggingIn();
            _cancellation = new CancellationTokenSource();
            LoginChallengeHandler challenges = new LoginChallengeHandler(
                new SteamGuardDeviceCodeProvider(_account),
                RequestEmailCodeAsync,
                message => Report(message),
                _cancellation.Token);
            try
            {
                SteamLoginResult result = await _login.LoginAgainAsync(_account, password, challenges, this, _cancellation.Token);
                if (result.Cancelled)
                {
                    _viewModel.MarkCancelled();
                    Close(false);
                    return;
                }

                if (!result.Succeeded || result.Session == null)
                {
                    _viewModel.MarkFailed(result.Error);
                    return;
                }

                if (_import)
                {
                    ResultSession = result.Session;
                    _viewModel.MarkSucceeded();
                    Close(true);
                    return;
                }

                string saveError = _commit == null ? "Unable to save refreshed session." : await _commit(result.Session, new EncryptionPrompt(this));
                if (!string.IsNullOrEmpty(saveError))
                {
                    _viewModel.MarkFailed(saveError);
                    return;
                }

                _viewModel.MarkSucceeded();
                Close(true);
            }
            catch (Exception)
            {
                _viewModel.MarkFailed("Steam login failed.");
            }
            finally
            {
                password = null;
                PasswordBox.Text = "";
                Interlocked.Exchange(ref _loginStarted, 0);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.State == LoginUiState.LoggingIn)
            {
                CancelLogin();
                return;
            }

            Close(false);
        }

        private void CancelLogin()
        {
            CancellationTokenSource cancellation = _cancellation;
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
    }
}
