using SDA.Desktop.Models;
using SDA.Desktop.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SDA.Desktop.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly AccountService _accounts;
        private readonly SettingsService _settings;
        private readonly ISteamClock _clock;
        private readonly IEncryptionPrompt _prompt;
        private readonly IFolderPicker _folders;
        private readonly IClipboardService _clipboard;
        private readonly string _defaultMaFilesDirectory;

        private IReadOnlyList<AccountViewModel> _accountList = new AccountViewModel[0];
        private AccountViewModel _selectedAccount;
        private string _currentCode = "—";
        private string _countdownText = "";
        private string _baseStatus = "";
        private bool _canCopyCode;
        private bool _showingCopied;
        private DateTime _copiedUntil;
        private long _steamTime = -1;
        private string _passKey;
        private string _directory;
        private int _refreshing;

        public MainWindowViewModel(
            AccountService accounts,
            SettingsService settings,
            ISteamClock clock,
            IEncryptionPrompt prompt,
            IFolderPicker folders,
            IClipboardService clipboard,
            string defaultMaFilesDirectory)
        {
            _accounts = accounts;
            _settings = settings;
            _clock = clock;
            _prompt = prompt;
            _folders = folders;
            _clipboard = clipboard;
            _defaultMaFilesDirectory = defaultMaFilesDirectory;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public IReadOnlyList<AccountViewModel> Accounts
        {
            get { return _accountList; }
            private set { SetField(ref _accountList, value); }
        }

        public AccountViewModel SelectedAccount
        {
            get { return _selectedAccount; }
            set
            {
                if (ReferenceEquals(_selectedAccount, value))
                {
                    return;
                }

                _selectedAccount = value;
                OnPropertyChanged();
                ApplyDisplay();
            }
        }

        public string CurrentCode
        {
            get { return _currentCode; }
            private set { SetField(ref _currentCode, value); }
        }

        public string CountdownText
        {
            get { return _countdownText; }
            private set { SetField(ref _countdownText, value); }
        }

        public bool CanCopyCode
        {
            get { return _canCopyCode; }
            private set { SetField(ref _canCopyCode, value); }
        }

        public string StatusText
        {
            get
            {
                if (_showingCopied && DateTime.UtcNow < _copiedUntil)
                {
                    return "Copied";
                }

                return _baseStatus;
            }
        }

        public async Task InitializeAsync()
        {
            try
            {
                AppSettings settings = _settings.Load();
                string saved = settings.MaFilesDirectory;
                string directory = _defaultMaFilesDirectory;
                bool savedMissing = false;
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    if (Directory.Exists(saved))
                    {
                        directory = saved;
                    }
                    else
                    {
                        savedMissing = true;
                    }
                }

                await LoadDirectoryAsync(directory, false);
                if (savedMissing)
                {
                    SetBaseStatus("Unable to load maFiles");
                }
            }
            catch (Exception)
            {
                SetBaseStatus("Unable to load maFiles");
            }
        }

        public async Task OpenFolderAsync()
        {
            string path = await _folders.PickAsync();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!_accounts.LooksLikeMaFilesFolder(path))
            {
                SetBaseStatus("Unable to load maFiles");
                return;
            }

            _passKey = null;
            await LoadDirectoryAsync(path, true);
        }

        public async Task RefreshCodesAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _refreshing, 1) == 1)
            {
                return;
            }

            try
            {
                RestoreCopiedStatus();
                _steamTime = await _clock.GetSteamTimeAsync();
                ApplyDisplay();
            }
            catch (Exception)
            {
                if (string.IsNullOrEmpty(_baseStatus))
                {
                    SetBaseStatus("Unable to load maFiles");
                }
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        public async Task CopyAsync()
        {
            if (!CanCopyCode || string.IsNullOrEmpty(CurrentCode) || CurrentCode == "—")
            {
                return;
            }

            await _clipboard.SetTextAsync(CurrentCode);
            _showingCopied = true;
            _copiedUntil = DateTime.UtcNow.AddSeconds(2);
            OnPropertyChanged(nameof(StatusText));
        }

        public void SetFailureStatus(string status)
        {
            SetBaseStatus(status);
        }

        public async Task LoadDirectoryAsync(string directory, bool persist)
        {
            _directory = directory;
            MaFilesLoadResult result = _accounts.Load(directory, _passKey);
            result = await ResolvePasswordAsync(directory, result);
            if (persist && result.RememberDirectory && !string.IsNullOrEmpty(directory))
            {
                _settings.Save(new AppSettings { MaFilesDirectory = directory });
            }

            ApplyLoad(result);
            await RefreshCodesAsync();
        }

        private async Task<MaFilesLoadResult> ResolvePasswordAsync(string directory, MaFilesLoadResult result)
        {
            string error = null;
            while (result.Kind == MaFilesLoadKind.PasswordRequired || result.Kind == MaFilesLoadKind.InvalidPassword)
            {
                if (result.Kind == MaFilesLoadKind.InvalidPassword)
                {
                    error = "Incorrect password.";
                }

                string password = await _prompt.PromptAsync(error);
                if (password == null)
                {
                    _passKey = null;
                    return new MaFilesLoadResult(MaFilesLoadKind.UnlockCancelled, new SteamAuth.SteamGuardAccount[0], "Unable to decrypt accounts", result.RememberDirectory);
                }

                _passKey = password;
                result = _accounts.Load(directory, _passKey);
            }

            return result;
        }

        private void ApplyLoad(MaFilesLoadResult result)
        {
            AccountViewModel[] items = result.Accounts
                .Select(account => new AccountViewModel(account))
                .OrderBy(account => account.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            Accounts = items;
            SetBaseStatus(result.StatusText);
            SelectedAccount = items.Length == 0 ? null : items[0];
        }

        private void ApplyDisplay()
        {
            if (SelectedAccount == null || _steamTime < 0)
            {
                CurrentCode = "—";
                CountdownText = "";
                CanCopyCode = false;
                return;
            }

            SteamGuardDisplayState state = SteamGuardDisplay.ForAccount(SelectedAccount.Account, _steamTime);
            if (!state.CanCopy)
            {
                CurrentCode = "—";
                CountdownText = "";
                CanCopyCode = false;
                SetBaseStatus(state.StatusText ?? "Account does not contain a valid authenticator");
                return;
            }

            CurrentCode = state.Code;
            CountdownText = "expires in " + state.SecondsRemaining + "s";
            CanCopyCode = true;
            if (_baseStatus == "Account does not contain a valid authenticator")
            {
                SetBaseStatus("");
            }
        }

        private void RestoreCopiedStatus()
        {
            if (_showingCopied && DateTime.UtcNow >= _copiedUntil)
            {
                _showingCopied = false;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private void SetBaseStatus(string status)
        {
            _baseStatus = status ?? "";
            if (!_showingCopied || DateTime.UtcNow >= _copiedUntil)
            {
                _showingCopied = false;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propertyName);
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
