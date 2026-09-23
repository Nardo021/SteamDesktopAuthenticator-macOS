using SDA.Desktop.Services;
using SteamAuth;
using SteamKit2.Authentication;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.ViewModels
{
    public enum EnrollmentUiState
    {
        Idle,
        Authenticating,
        ConfirmContinue,
        AddingAuthenticator,
        NeedPhoneNumber,
        NeedEmailConfirmation,
        NeedNewEncryptionPasskey,
        NeedExistingEncryptionPasskey,
        ShowRevocationCode,
        ConfirmRevocationCode,
        NeedSmsCode,
        Finalizing,
        Completed,
        Failed,
        Cancelled
    }

    public sealed class SetupAccountWindowViewModel : INotifyPropertyChanged, ILoginStatus
    {
        private readonly ISteamLoginService _login;
        private readonly IAuthenticatorLinkerFactory _linkers;
        private readonly EnrollmentPersistenceService _persistence;
        private readonly string _directory;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private EnrollmentUiState _state = EnrollmentUiState.Idle;
        private string _username = "";
        private string _statusText = "";
        private string _emailAddress = "";
        private string _revocationCode = "";
        private string _phoneError = "";
        private string _passKey;
        private bool _encrypt;
        private bool _savedInitially;
        private bool _finalizedOnSteam;
        private bool _closed;
        private int _busy;
        private SessionData _session;
        private IAuthenticatorLinker _linker;

        public SetupAccountWindowViewModel(
            ISteamLoginService login,
            IAuthenticatorLinkerFactory linkers,
            EnrollmentPersistenceService persistence,
            string directory,
            string currentPassKey)
        {
            _login = login;
            _linkers = linkers ?? new SteamAuthAuthenticatorLinkerFactory();
            _persistence = persistence ?? new EnrollmentPersistenceService();
            _directory = directory;
            _passKey = currentPassKey;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Username
        {
            get { return _username; }
            set
            {
                if (_username == value)
                {
                    return;
                }

                _username = value ?? "";
                OnPropertyChanged();
            }
        }

        public EnrollmentUiState State
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
                OnPropertyChanged(nameof(CanLogin));
                OnPropertyChanged(nameof(IsLoginVisible));
                OnPropertyChanged(nameof(IsConfirmContinueVisible));
                OnPropertyChanged(nameof(IsBusyVisible));
                OnPropertyChanged(nameof(IsEmailVisible));
                OnPropertyChanged(nameof(IsNewPasskeyVisible));
                OnPropertyChanged(nameof(IsExistingPasskeyVisible));
                OnPropertyChanged(nameof(IsRevocationVisible));
                OnPropertyChanged(nameof(IsConfirmRevocationVisible));
                OnPropertyChanged(nameof(IsSmsVisible));
                OnPropertyChanged(nameof(IsFinishedVisible));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(EmailPrompt));
                OnPropertyChanged(nameof(FinishedTitle));
                OnPropertyChanged(nameof(FinishedDetail));
            }
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

        public string PhoneError
        {
            get { return _phoneError; }
            private set
            {
                _phoneError = value ?? "";
                OnPropertyChanged();
            }
        }

        public string RevocationCode
        {
            get { return _revocationCode; }
            private set
            {
                _revocationCode = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(FinishedDetail));
            }
        }

        public string UsedPassKey
        {
            get { return _passKey; }
        }

        public string EnrolledAccountName
        {
            get { return _linker == null || _linker.LinkedAccount == null ? "" : _linker.LinkedAccount.AccountName; }
        }

        public string LoginButtonText
        {
            get { return State == EnrollmentUiState.Authenticating ? "Logging in..." : "Login"; }
        }

        public bool CanLogin
        {
            get { return State == EnrollmentUiState.Idle || (State == EnrollmentUiState.Failed && _session == null && !_savedInitially); }
        }

        public bool CanCancel
        {
            get { return State != EnrollmentUiState.Completed && State != EnrollmentUiState.Finalizing; }
        }

        public bool IsLoginVisible
        {
            get { return State == EnrollmentUiState.Idle || State == EnrollmentUiState.Authenticating || (State == EnrollmentUiState.Failed && _session == null && !_savedInitially); }
        }

        public bool IsConfirmContinueVisible
        {
            get { return State == EnrollmentUiState.ConfirmContinue; }
        }

        public bool IsBusyVisible
        {
            get { return State == EnrollmentUiState.AddingAuthenticator || State == EnrollmentUiState.Finalizing; }
        }

        public bool IsEmailVisible
        {
            get { return State == EnrollmentUiState.NeedEmailConfirmation; }
        }

        public bool IsNewPasskeyVisible
        {
            get { return State == EnrollmentUiState.NeedNewEncryptionPasskey; }
        }

        public bool IsExistingPasskeyVisible
        {
            get { return State == EnrollmentUiState.NeedExistingEncryptionPasskey; }
        }

        public bool IsRevocationVisible
        {
            get { return State == EnrollmentUiState.ShowRevocationCode; }
        }

        public bool IsConfirmRevocationVisible
        {
            get { return State == EnrollmentUiState.ConfirmRevocationCode; }
        }

        public bool IsSmsVisible
        {
            get { return State == EnrollmentUiState.NeedSmsCode; }
        }

        public bool IsFinishedVisible
        {
            get
            {
                if (State == EnrollmentUiState.Completed || State == EnrollmentUiState.Cancelled)
                {
                    return true;
                }

                return State == EnrollmentUiState.Failed && !IsLoginVisible;
            }
        }

        public string EmailPrompt
        {
            get
            {
                if (string.IsNullOrEmpty(_emailAddress))
                {
                    return AuthenticatorEnrollmentService.EmailConfirmationMessage;
                }

                return AuthenticatorEnrollmentService.EmailConfirmationMessage + "\n" + _emailAddress;
            }
        }

        public string FinishedTitle
        {
            get
            {
                if (State == EnrollmentUiState.Completed)
                {
                    return AuthenticatorEnrollmentService.SuccessMessage;
                }

                if (State == EnrollmentUiState.Cancelled)
                {
                    return "Setup New Account cancelled.";
                }

                return string.IsNullOrEmpty(StatusText) ? AuthenticatorEnrollmentService.GeneralLinkFailureMessage : StatusText;
            }
        }

        public string FinishedDetail
        {
            get
            {
                if (string.IsNullOrEmpty(RevocationCode))
                {
                    return "";
                }

                return RevocationCode;
            }
        }

        public IAuthenticatorLinker Linker
        {
            get { return _linker; }
        }

        public bool SavedInitially
        {
            get { return _savedInitially; }
        }

        public void Report(string status)
        {
            StatusText = status;
        }

        public async Task LoginAsync(string password, IAuthenticator challenges)
        {
            if (!CanLogin || !TryBeginBusy())
            {
                return;
            }

            State = EnrollmentUiState.Authenticating;
            StatusText = "Logging in...";
            try
            {
                SteamLoginResult result = await _login.AuthenticateCredentialsAsync(Username, password, challenges, this, _lifetime.Token);
                if (_closed)
                {
                    return;
                }

                if (result.Cancelled)
                {
                    MarkCancelled();
                    return;
                }

                if (!result.Succeeded || result.Session == null)
                {
                    Fail(string.IsNullOrEmpty(result.Error) ? "Steam login failed." : result.Error, false);
                    return;
                }

                _session = result.Session;
                State = EnrollmentUiState.ConfirmContinue;
                StatusText = "Steam account login succeeded. Continue adding Steam Desktop Authenticator as the mobile authenticator for this account?";
            }
            catch (OperationCanceledException)
            {
                MarkCancelled();
            }
            catch (Exception)
            {
                Fail("Steam login failed.", false);
            }
            finally
            {
                EndBusy();
            }
        }

        public async Task ConfirmContinueAsync()
        {
            if (State != EnrollmentUiState.ConfirmContinue || _session == null)
            {
                return;
            }

            if (_linker == null)
            {
                _linker = _linkers.Create(_session);
            }

            await RunAddAuthenticatorAsync();
        }

        public void DeclineContinue()
        {
            if (State != EnrollmentUiState.ConfirmContinue)
            {
                return;
            }

            _linker = null;
            MarkCancelled();
        }

        public async Task SubmitPhoneAsync(string phoneNumber, string countryCode)
        {
            if (_linker == null)
            {
                return;
            }

            PhoneValidationResult phone = AuthenticatorEnrollmentService.ValidatePhone(phoneNumber, countryCode);
            if (!phone.Valid)
            {
                PhoneError = phone.Error;
                State = EnrollmentUiState.NeedPhoneNumber;
                return;
            }

            PhoneError = "";
            AuthenticatorEnrollmentService.ApplyPhone(_linker, phone.PhoneNumber, phone.CountryCode);
            await RunAddAuthenticatorAsync();
        }

        public Task ConfirmEmailAsync()
        {
            return RunAddAuthenticatorAsync();
        }

        public async Task SubmitNewPasskeyAsync(string passKey)
        {
            if (State != EnrollmentUiState.NeedNewEncryptionPasskey)
            {
                return;
            }

            _encrypt = !string.IsNullOrEmpty(passKey);
            _passKey = _encrypt ? passKey : null;
            await SaveInitialAndContinueAsync();
        }

        public async Task SubmitExistingPasskeyAsync(string passKey)
        {
            if (State != EnrollmentUiState.NeedExistingEncryptionPasskey)
            {
                return;
            }

            if (string.IsNullOrEmpty(passKey) || _persistence.Inspect(_directory, passKey).Kind != EnrollmentEncryptionKind.UseExistingKey)
            {
                StatusText = "Incorrect password.";
                return;
            }

            _encrypt = true;
            _passKey = passKey;
            await SaveInitialAndContinueAsync();
        }

        public void ContinueRevocationDisplay()
        {
            if (State != EnrollmentUiState.ShowRevocationCode)
            {
                return;
            }

            State = EnrollmentUiState.ConfirmRevocationCode;
        }

        public async Task SubmitRevocationConfirmationAsync(string entered)
        {
            if (State != EnrollmentUiState.ConfirmRevocationCode || _linker == null || _linker.LinkedAccount == null)
            {
                return;
            }

            if (!AuthenticatorEnrollmentService.RevocationMatches(entered, _linker.LinkedAccount.RevocationCode))
            {
                RollbackLocalAccount();
                Fail(AuthenticatorEnrollmentService.RevocationIncorrectMessage, true);
                return;
            }

            State = EnrollmentUiState.NeedSmsCode;
            StatusText = AuthenticatorEnrollmentService.SmsCodePrompt;
            await Task.CompletedTask;
        }

        public async Task SubmitSmsAsync(string smsCode)
        {
            if (State != EnrollmentUiState.NeedSmsCode || _linker == null || !TryBeginBusy())
            {
                return;
            }

            State = EnrollmentUiState.Finalizing;
            StatusText = "Finalizing authenticator...";
            try
            {
                AuthenticatorLinker.FinalizeResult result = await AuthenticatorEnrollmentService.FinalizeAsync(_linker, smsCode, _lifetime.Token);
                if (_closed)
                {
                    return;
                }

                if (result == AuthenticatorLinker.FinalizeResult.BadSMSCode)
                {
                    State = EnrollmentUiState.NeedSmsCode;
                    StatusText = "The SMS code was incorrect. Please try again.";
                    return;
                }

                if (result == AuthenticatorLinker.FinalizeResult.UnableToGenerateCorrectCodes)
                {
                    CaptureRevocation();
                    RollbackLocalAccount();
                    Fail(AuthenticatorEnrollmentService.UnableToGenerateCodesMessage, true);
                    return;
                }

                if (result != AuthenticatorLinker.FinalizeResult.Success)
                {
                    CaptureRevocation();
                    RollbackLocalAccount();
                    Fail(AuthenticatorEnrollmentService.FinalizeFailedMessage, true);
                    return;
                }

                _finalizedOnSteam = true;
                EnrollmentSaveResult saved = _persistence.SaveAccount(_linker.LinkedAccount, _directory, _encrypt, _passKey);
                if (saved.Status != EnrollmentSaveStatus.Saved)
                {
                    CaptureRevocation();
                    Fail(AuthenticatorEnrollmentService.FinalSaveFailedMessage, true);
                    return;
                }

                CaptureRevocation();
                StatusText = AuthenticatorEnrollmentService.SuccessMessage;
                State = EnrollmentUiState.Completed;
            }
            catch (OperationCanceledException)
            {
                MarkCancelled();
            }
            catch (Exception)
            {
                CaptureRevocation();
                RollbackLocalAccount();
                Fail(AuthenticatorEnrollmentService.FinalizeFailedMessage, true);
            }
            finally
            {
                EndBusy();
            }
        }

        public void Cancel()
        {
            if (!CanCancel)
            {
                return;
            }

            if (_savedInitially && !_finalizedOnSteam)
            {
                RollbackLocalAccount();
            }

            if (_linker != null && _linker.LinkedAccount == null)
            {
                _linker = null;
            }

            MarkCancelled();
            try
            {
                _lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Close()
        {
            _closed = true;
            try
            {
                _lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task RunAddAuthenticatorAsync()
        {
            if (_linker == null || !TryBeginBusy())
            {
                return;
            }

            State = EnrollmentUiState.AddingAuthenticator;
            StatusText = "Adding authenticator...";
            try
            {
                AuthenticatorLinker.LinkResult result;
                while (true)
                {
                    result = await AuthenticatorEnrollmentService.AddAuthenticatorAsync(_linker, _lifetime.Token);
                    if (_closed)
                    {
                        return;
                    }

                    if (result != AuthenticatorLinker.LinkResult.MustRemovePhoneNumber)
                    {
                        break;
                    }

                    AuthenticatorEnrollmentService.ClearPhone(_linker);
                }

                await ApplyLinkResultAsync(result);
            }
            catch (OperationCanceledException)
            {
                MarkCancelled();
            }
            catch (Exception)
            {
                Fail(AuthenticatorEnrollmentService.GeneralLinkFailureMessage, false);
            }
            finally
            {
                EndBusy();
            }
        }

        private async Task ApplyLinkResultAsync(AuthenticatorLinker.LinkResult result)
        {
            switch (result)
            {
                case AuthenticatorLinker.LinkResult.MustProvidePhoneNumber:
                    State = EnrollmentUiState.NeedPhoneNumber;
                    StatusText = "Enter the phone number Steam should use for this authenticator.";
                    return;
                case AuthenticatorLinker.LinkResult.MustConfirmEmail:
                    _emailAddress = _linker == null ? "" : _linker.ConfirmationEmailAddress ?? "";
                    OnPropertyChanged(nameof(EmailPrompt));
                    State = EnrollmentUiState.NeedEmailConfirmation;
                    StatusText = AuthenticatorEnrollmentService.EmailConfirmationMessage;
                    return;
                case AuthenticatorLinker.LinkResult.FailureAddingPhone:
                    AuthenticatorEnrollmentService.ClearPhone(_linker);
                    PhoneError = AuthenticatorEnrollmentService.PhoneAddFailedMessage;
                    State = EnrollmentUiState.NeedPhoneNumber;
                    StatusText = AuthenticatorEnrollmentService.PhoneAddFailedMessage;
                    return;
                case AuthenticatorLinker.LinkResult.AuthenticatorPresent:
                    Fail(AuthenticatorEnrollmentService.AuthenticatorPresentMessage, false);
                    return;
                case AuthenticatorLinker.LinkResult.AwaitingFinalization:
                    await BeginLocalStorageAsync();
                    return;
                default:
                    Fail(AuthenticatorEnrollmentService.GeneralLinkFailureMessage, false);
                    return;
            }
        }

        private async Task BeginLocalStorageAsync()
        {
            if (_linker == null || _linker.LinkedAccount == null)
            {
                Fail(AuthenticatorEnrollmentService.GeneralLinkFailureMessage, false);
                return;
            }

            if (string.IsNullOrEmpty(_linker.LinkedAccount.AccountName) && !string.IsNullOrEmpty(Username))
            {
                _linker.LinkedAccount.AccountName = Username;
            }

            EnrollmentEncryptionPlan plan = _persistence.Inspect(_directory, _passKey);
            if (plan.Kind == EnrollmentEncryptionKind.AskNewPasskey)
            {
                State = EnrollmentUiState.NeedNewEncryptionPasskey;
                StatusText = "Encryption passkey";
                return;
            }

            if (plan.Kind == EnrollmentEncryptionKind.AskExistingKey)
            {
                State = EnrollmentUiState.NeedExistingEncryptionPasskey;
                StatusText = "Please enter your current encryption passkey.";
                return;
            }

            _encrypt = plan.Encrypt;
            await SaveInitialAndContinueAsync();
        }

        private async Task SaveInitialAndContinueAsync()
        {
            if (_linker == null || _linker.LinkedAccount == null)
            {
                Fail(AuthenticatorEnrollmentService.InitialSaveFailedMessage, false);
                return;
            }

            EnrollmentSaveResult saved = _persistence.SaveAccount(_linker.LinkedAccount, _directory, _encrypt, _passKey);
            if (saved.Status != EnrollmentSaveStatus.Saved)
            {
                _persistence.RemoveAccount(_linker.LinkedAccount, _directory);
                Fail(AuthenticatorEnrollmentService.InitialSaveFailedMessage, false);
                return;
            }

            _savedInitially = true;
            CaptureRevocation();
            State = EnrollmentUiState.ShowRevocationCode;
            StatusText = AuthenticatorEnrollmentService.RevocationSavePrompt;
            await Task.CompletedTask;
        }

        private void CaptureRevocation()
        {
            if (_linker != null && _linker.LinkedAccount != null)
            {
                RevocationCode = _linker.LinkedAccount.RevocationCode ?? "";
            }
        }

        private void RollbackLocalAccount()
        {
            if (_finalizedOnSteam || _linker == null || _linker.LinkedAccount == null)
            {
                return;
            }

            _persistence.RemoveAccount(_linker.LinkedAccount, _directory);
            _savedInitially = false;
        }

        private void Fail(string message, bool keepRevocation)
        {
            StatusText = message;
            if (!keepRevocation)
            {
                RevocationCode = "";
                _linker = null;
            }

            State = EnrollmentUiState.Failed;
        }

        private void MarkCancelled()
        {
            if (State == EnrollmentUiState.Completed || State == EnrollmentUiState.Failed)
            {
                return;
            }

            StatusText = "Setup New Account cancelled.";
            State = EnrollmentUiState.Cancelled;
        }

        private bool TryBeginBusy()
        {
            return Interlocked.Exchange(ref _busy, 1) == 0;
        }

        private void EndBusy()
        {
            Interlocked.Exchange(ref _busy, 0);
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
