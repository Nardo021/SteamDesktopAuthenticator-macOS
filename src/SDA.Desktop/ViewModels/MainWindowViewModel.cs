using SDA.Core.Storage;
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
        private readonly SessionRefreshService _sessionRefresh;
        private readonly SessionPersistenceService _persistence;
        private readonly string _cliEncryptionKey;

        private IReadOnlyList<AccountViewModel> _allAccounts = new AccountViewModel[0];
        private IReadOnlyList<AccountViewModel> _accountList = new AccountViewModel[0];
        private AccountViewModel _selectedAccount;
        private string _searchText = "";
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
        private int _sessionOperation;
        private bool _manifestEncrypted;
        private int _accountCount;

        public MainWindowViewModel(
            AccountService accounts,
            SettingsService settings,
            ISteamClock clock,
            IEncryptionPrompt prompt,
            IFolderPicker folders,
            IClipboardService clipboard,
            string defaultMaFilesDirectory,
            IAccessTokenRefresher accessTokenRefresher = null,
            SessionPersistenceService persistence = null,
            string cliEncryptionKey = null)
        {
            _accounts = accounts;
            _settings = settings;
            _clock = clock;
            _prompt = prompt;
            _folders = folders;
            _clipboard = clipboard;
            _defaultMaFilesDirectory = defaultMaFilesDirectory;
            _sessionRefresh = new SessionRefreshService(accessTokenRefresher);
            _persistence = persistence ?? new SessionPersistenceService();
            _cliEncryptionKey = cliEncryptionKey;
        }

        public const string ClockFailedMessage = "Unable to refresh the Steam Guard timer. Check the connection and try again.";

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler ManifestContextChanged;

        public IReadOnlyList<AccountViewModel> AllAccounts
        {
            get { return _allAccounts; }
        }

        public IReadOnlyList<AccountViewModel> Accounts
        {
            get { return _accountList; }
            private set { SetField(ref _accountList, value); }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                string next = value ?? "";
                if (_searchText == next)
                {
                    return;
                }

                _searchText = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFilterActive));
                OnPropertyChanged(nameof(CanReorderAccounts));
                ApplyFilter();
            }
        }

        public bool IsFilterActive
        {
            get { return !string.IsNullOrEmpty(_searchText); }
        }

        public bool CanReorderAccounts
        {
            get { return !IsFilterActive && SelectedAccount != null && _allAccounts.Count > 0; }
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
                OnPropertyChanged(nameof(CanUseSessionActions));
                OnPropertyChanged(nameof(CanViewConfirmations));
                OnPropertyChanged(nameof(CanReorderAccounts));
                ApplyDisplay();
            }
        }

        public bool CanUseSessionActions
        {
            get { return SelectedAccount != null && _sessionOperation == 0; }
        }

        public bool CanViewConfirmations
        {
            get { return SelectedAccount != null; }
        }

        public bool ManifestEncrypted
        {
            get { return _manifestEncrypted; }
        }

        public int AccountCount
        {
            get { return _accountCount; }
        }

        public bool CanManageEncryption
        {
            get { return _accountCount > 0 && _sessionOperation == 0; }
        }

        public string EncryptionMenuText
        {
            get { return _manifestEncrypted ? "Manage Encryption" : "Setup Encryption"; }
        }

        public string VersionLabel
        {
            get { return SDA.Desktop.AppVersion.DisplayLabel; }
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
                await LoadDirectoryAsync(
                    MaFilesDirectoryPolicy.ResolveStartupDirectory(
                        settings.MaFilesDirectory,
                        _defaultMaFilesDirectory),
                    false);
            }
            catch (Exception)
            {
                SetBaseStatus(AccountService.UnableToLoadMessage);
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
                SetBaseStatus(AccountService.UnableToLoadMessage);
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
                    SetBaseStatus(ClockFailedMessage);
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

        public bool TryBeginSessionOperation()
        {
            if (SelectedAccount == null || System.Threading.Interlocked.Exchange(ref _sessionOperation, 1) == 1)
            {
                return false;
            }

            OnPropertyChanged(nameof(CanUseSessionActions));
            OnPropertyChanged(nameof(CanManageEncryption));
            return true;
        }

        public void EndSessionOperation()
        {
            System.Threading.Interlocked.Exchange(ref _sessionOperation, 0);
            OnPropertyChanged(nameof(CanUseSessionActions));
            OnPropertyChanged(nameof(CanManageEncryption));
        }

        public async Task<string> CommitRefreshedSessionAsync(SteamAuth.SessionData session, IEncryptionPrompt prompt)
        {
            if (SelectedAccount == null || SelectedAccount.Account == null)
            {
                return "Unable to save refreshed session.";
            }

            string error = await PersistSessionAsync(SelectedAccount.Account, session, true, prompt);
            if (error == null)
            {
                SetBaseStatus("Login successful.");
            }
            else
            {
                SetBaseStatus(error);
            }

            return error;
        }

        public string CurrentDirectory
        {
            get { return _directory; }
        }

        public string CurrentPassKey
        {
            get { return _passKey; }
        }

        public async Task ReloadAfterEnrollmentAsync(string accountName, string passKey)
        {
            if (!string.IsNullOrEmpty(passKey))
            {
                _passKey = passKey;
            }

            await ReloadPreservingSelectionAsync(accountName, 0);
        }

        public void SetActivePassKey(string passKey)
        {
            _passKey = passKey;
        }

        public ManifestRuntimeSettings ReadRuntimeSettings()
        {
            return new ManifestSettingsService().Read(_directory);
        }

        public async Task<bool> TryMoveSelectedAsync(AccountMoveDirection direction)
        {
            if (IsFilterActive || SelectedAccount == null || string.IsNullOrEmpty(_directory) || _allAccounts.Count == 0)
            {
                return false;
            }

            int from = IndexOfAccount(_allAccounts, SelectedAccount);
            if (from < 0)
            {
                return false;
            }

            int to = direction == AccountMoveDirection.Up ? from - 1 : from + 1;
            if (to < 0 || to >= _allAccounts.Count)
            {
                return false;
            }

            string name = SelectedAccount.Account == null ? null : SelectedAccount.Account.AccountName;
            ulong steamId = SelectedAccount.SteamId;
            bool moved;
            try
            {
                moved = ManifestMutationGate.Shared.Run(() =>
                {
                    Manifest manifest = Manifest.GetManifest(_directory);
                    if (manifest.Entries == null || from >= manifest.Entries.Count || to >= manifest.Entries.Count)
                    {
                        return false;
                    }

                    manifest.MoveEntry(from, to);
                    return true;
                });
            }
            catch (Exception)
            {
                return false;
            }

            if (!moved)
            {
                return false;
            }

            await ReloadPreservingSelectionAsync(name, steamId);
            return true;
        }

        public async Task ReloadPreservingSelectionAsync(string accountName, ulong steamId)
        {
            ulong previousId = steamId;
            string previousName = accountName;
            if (previousId == 0 && SelectedAccount != null && SelectedAccount.Account != null && SelectedAccount.Account.Session != null)
            {
                previousId = SelectedAccount.Account.Session.SteamID;
            }

            if (string.IsNullOrEmpty(previousName) && SelectedAccount != null && SelectedAccount.Account != null)
            {
                previousName = SelectedAccount.Account.AccountName;
            }

            await LoadDirectoryAsync(_directory, false);
            if (Accounts == null)
            {
                return;
            }

            foreach (AccountViewModel item in Accounts)
            {
                if (item == null || item.Account == null)
                {
                    continue;
                }

                if (previousId != 0 && item.Account.Session != null && item.Account.Session.SteamID == previousId)
                {
                    SelectedAccount = item;
                    return;
                }
            }

            foreach (AccountViewModel item in Accounts)
            {
                if (item != null && item.Account != null && string.Equals(item.Account.AccountName, previousName, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedAccount = item;
                    return;
                }
            }
        }

        public Task<string> PersistUpdatedSessionAsync(SteamAuth.SteamGuardAccount account, SteamAuth.SessionData session, IEncryptionPrompt prompt)
        {
            if (account == null || session == null)
            {
                return Task.FromResult("Unable to save refreshed session.");
            }

            return PersistSessionAsync(account, session, false, prompt);
        }

        public async Task ForceRefreshAsync()
        {
            if (!TryBeginSessionOperation())
            {
                return;
            }

            SteamAuth.SteamGuardAccount account = SelectedAccount.Account;
            try
            {
                if (!_sessionRefresh.CanForceRefresh(account == null ? null : account.Session))
                {
                    SetBaseStatus("Session cannot be refreshed. Use Login Again.");
                    return;
                }

                SetBaseStatus("Refreshing session...");
                SteamAuth.SessionData previous = SessionPersistenceService.Clone(account.Session);
                try
                {
                    await _sessionRefresh.RefreshAsync(account.Session, System.Threading.CancellationToken.None);
                    string error = await PersistSessionAsync(account, account.Session, false, _prompt);
                    if (error != null)
                    {
                        account.Session = previous;
                        SetBaseStatus(error);
                        return;
                    }

                    SetBaseStatus("Session refreshed.");
                }
                catch (System.Exception)
                {
                    account.Session = previous;
                    SetBaseStatus("Unable to refresh session.");
                }
            }
            finally
            {
                EndSessionOperation();
            }
        }

        private async Task<string> PersistSessionAsync(SteamAuth.SteamGuardAccount account, SteamAuth.SessionData session, bool markFullyEnrolled, IEncryptionPrompt prompt)
        {
            string key = _passKey;
            IEncryptionPrompt encryptionPrompt = prompt ?? _prompt;
            while (true)
            {
                SessionSaveResult saved = _persistence.Commit(account, session, _directory, key, markFullyEnrolled);
                if (saved.Status == SessionSaveStatus.Saved)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        _passKey = key;
                    }

                    return null;
                }

                if (saved.Status != SessionSaveStatus.KeyRequired && saved.Status != SessionSaveStatus.InvalidKey)
                {
                    return "Unable to save refreshed session.";
                }

                if (encryptionPrompt == null)
                {
                    return "Unable to save refreshed session.";
                }

                string entered = await encryptionPrompt.PromptAsync(saved.Status == SessionSaveStatus.InvalidKey ? "Incorrect password." : null);
                if (string.IsNullOrEmpty(entered))
                {
                    return "Unable to save refreshed session.";
                }

                key = entered;
            }
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
            if (result.Kind == MaFilesLoadKind.PasswordRequired && !string.IsNullOrEmpty(_cliEncryptionKey))
            {
                MaFilesLoadResult cliResult = _accounts.Load(directory, _cliEncryptionKey);
                if (cliResult.Kind == MaFilesLoadKind.Success || cliResult.Kind == MaFilesLoadKind.NoAccounts)
                {
                    _passKey = _cliEncryptionKey;
                    return cliResult;
                }
            }

            string error = null;
            while (result.Kind == MaFilesLoadKind.PasswordRequired || result.Kind == MaFilesLoadKind.InvalidPassword)
            {
                if (result.Kind == MaFilesLoadKind.InvalidPassword)
                {
                    error = EncryptionManagementService.WrongCurrentKeyMessage;
                }

                string password = await _prompt.PromptAsync(error);
                if (password == null)
                {
                    _passKey = null;
                    return new MaFilesLoadResult(MaFilesLoadKind.UnlockCancelled, new SteamAuth.SteamGuardAccount[0], AccountService.UnableToDecryptMessage, result.RememberDirectory);
                }

                _passKey = password;
                result = _accounts.Load(directory, _passKey);
            }

            return result;
        }

        private void ApplyLoad(MaFilesLoadResult result)
        {
            _allAccounts = result.Accounts
                .Select(account => new AccountViewModel(account))
                .ToArray();
            _accountCount = _allAccounts.Count;
            _manifestEncrypted = ReadEncrypted(_directory);
            OnPropertyChanged(nameof(AllAccounts));
            OnPropertyChanged(nameof(AccountCount));
            OnPropertyChanged(nameof(ManifestEncrypted));
            OnPropertyChanged(nameof(CanManageEncryption));
            OnPropertyChanged(nameof(EncryptionMenuText));
            OnPropertyChanged(nameof(CanReorderAccounts));
            SetBaseStatus(result.StatusText);
            ApplyFilter();
            EventHandler handler = ManifestContextChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void ApplyFilter()
        {
            AccountViewModel previous = SelectedAccount;
            List<AccountViewModel> visible = new List<AccountViewModel>();
            foreach (AccountViewModel item in _allAccounts)
            {
                if (item != null && AccountFilter.Matches(item.DisplayName, _searchText))
                {
                    visible.Add(item);
                }
            }

            Accounts = visible;
            AccountViewModel preserved = FindMatchingAccount(visible, previous);
            if (preserved != null)
            {
                SelectedAccount = preserved;
            }
            else if (visible.Count > 0)
            {
                SelectedAccount = visible[0];
            }
            else
            {
                SelectedAccount = null;
            }
        }

        private static AccountViewModel FindMatchingAccount(IReadOnlyList<AccountViewModel> items, AccountViewModel target)
        {
            if (target == null || items == null)
            {
                return null;
            }

            foreach (AccountViewModel item in items)
            {
                if (SameAccount(item, target))
                {
                    return item;
                }
            }

            return null;
        }

        private static int IndexOfAccount(IReadOnlyList<AccountViewModel> items, AccountViewModel target)
        {
            if (items == null || target == null)
            {
                return -1;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (SameAccount(items[i], target))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool SameAccount(AccountViewModel left, AccountViewModel right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (left.SteamId != 0 && right.SteamId != 0 && left.SteamId == right.SteamId)
            {
                return true;
            }

            string leftName = left.Account == null ? null : left.Account.AccountName;
            string rightName = right.Account == null ? null : right.Account.AccountName;
            return !string.IsNullOrEmpty(leftName)
                && string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReadEncrypted(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            try
            {
                SDA.Core.Storage.Manifest manifest = SDA.Core.Storage.Manifest.GetManifest(directory);
                return manifest != null && manifest.Encrypted;
            }
            catch (Exception)
            {
                return false;
            }
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
