using Newtonsoft.Json;
using SDA.Core.Storage;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class SessionTests : IDisposable
    {
        private readonly string _root;

        public SessionTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-session-").FullName;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        [Fact]
        public void SessionState_ClassifiesSyntheticTokens()
        {
            Assert.Equal(SessionTokenState.Missing, SessionStateInspector.Inspect(null));
            Assert.Equal(SessionTokenState.Missing, SessionStateInspector.Inspect(new SessionData()));
            Assert.Equal(SessionTokenState.Missing, SessionStateInspector.Inspect(new SessionData { RefreshToken = "" }));

            SessionData expiredRefresh = TokenSession(SyntheticJwt.Expired(), SyntheticJwt.Valid());
            Assert.Equal(SessionTokenState.RefreshTokenExpired, SessionStateInspector.Inspect(expiredRefresh));
            Assert.False(SessionStateInspector.CanForceRefresh(expiredRefresh));

            SessionData expiredAccess = TokenSession(SyntheticJwt.Valid(), SyntheticJwt.Expired());
            Assert.Equal(SessionTokenState.AccessTokenExpired, SessionStateInspector.Inspect(expiredAccess));
            Assert.True(SessionStateInspector.CanForceRefresh(expiredAccess));

            SessionData valid = TokenSession(SyntheticJwt.Valid(), SyntheticJwt.Valid());
            Assert.Equal(SessionTokenState.Valid, SessionStateInspector.Inspect(valid));
            Assert.True(SessionStateInspector.CanForceRefresh(valid));

            SessionData malformed = TokenSession("not-a-jwt", SyntheticJwt.Valid());
            Assert.Equal(SessionTokenState.RefreshTokenExpired, SessionStateInspector.Inspect(malformed));
        }

        [Fact]
        public void Commit_ReplacesSessionAndPreservesSecrets()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            string sharedSecret = account.SharedSecret;
            string identitySecret = account.IdentitySecret;
            string revocation = account.RevocationCode;
            string name = account.AccountName;
            Manifest.GetManifest(directory).SaveAccount(account, false);

            SessionData refreshed = TokenSession(SyntheticJwt.Valid(), SyntheticJwt.Valid());
            refreshed.SteamID = account.Session.SteamID;
            SessionSaveResult result = new SessionPersistenceService().Commit(account, refreshed, directory, null, true);

            Assert.Equal(SessionSaveStatus.Saved, result.Status);
            Assert.Same(refreshed, account.Session);
            Assert.True(account.FullyEnrolled);
            Assert.Equal(sharedSecret, account.SharedSecret);
            Assert.Equal(identitySecret, account.IdentitySecret);
            Assert.Equal(revocation, account.RevocationCode);
            Assert.Equal(name, account.AccountName);

            SteamGuardAccount reloaded = Manifest.GetManifest(directory).LoadAccounts(null).Accounts[0];
            Assert.Equal(refreshed.AccessToken, reloaded.Session.AccessToken);
            Assert.Equal(refreshed.RefreshToken, reloaded.Session.RefreshToken);
            Assert.Equal(sharedSecret, reloaded.SharedSecret);
            Assert.Equal(name, reloaded.AccountName);
        }

        [Fact]
        public void Commit_EncryptedSessionStaysEncrypted()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            Assert.True(Manifest.GetManifest(directory).SaveAccount(account, true, "fixture-pass"));
            string maFile = Path.Combine(directory, account.Session.SteamID + ".maFile");
            SessionData refreshed = TokenSession(SyntheticJwt.Valid(), SyntheticJwt.Valid());
            refreshed.SteamID = account.Session.SteamID;

            SessionSaveResult result = new SessionPersistenceService().Commit(account, refreshed, directory, "fixture-pass", true);

            string stored = File.ReadAllText(maFile);
            Assert.Equal(SessionSaveStatus.Saved, result.Status);
            Assert.False(stored.TrimStart().StartsWith("{"));
            Assert.DoesNotContain(refreshed.AccessToken, stored);
            Assert.DoesNotContain("shared_secret", stored);
            SteamGuardAccount reloaded = Manifest.GetManifest(directory).LoadAccounts("fixture-pass").Accounts[0];
            Assert.Equal(refreshed.AccessToken, reloaded.Session.AccessToken);
            Assert.Equal(account.SharedSecret, reloaded.SharedSecret);
        }

        [Fact]
        public void Commit_WrongOrMissingKeyDoesNotWritePlaintext()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            Assert.True(Manifest.GetManifest(directory).SaveAccount(account, true, "fixture-pass"));
            string maFile = Path.Combine(directory, account.Session.SteamID + ".maFile");
            string before = File.ReadAllText(maFile);
            SessionData original = SessionPersistenceService.Clone(account.Session);
            SessionData refreshed = TokenSession(SyntheticJwt.Valid(), SyntheticJwt.Valid());
            refreshed.SteamID = account.Session.SteamID;

            SessionPersistenceService persistence = new SessionPersistenceService();
            Assert.Equal(SessionSaveStatus.KeyRequired, persistence.Commit(account, refreshed, directory, null, true).Status);
            Assert.Equal(SessionSaveStatus.InvalidKey, persistence.Commit(account, refreshed, directory, "wrong-passkey", true).Status);

            Assert.Equal(before, File.ReadAllText(maFile));
            Assert.False(File.ReadAllText(maFile).TrimStart().StartsWith("{"));
            Assert.Equal(original.AccessToken, account.Session.AccessToken);
            Assert.DoesNotContain(refreshed.AccessToken, File.ReadAllText(maFile));
        }

        [Fact]
        public void Commit_SaveFailureRestoresPreviousSessionAndFile()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            Manifest.GetManifest(directory).SaveAccount(account, false);
            string maFile = Path.Combine(directory, account.Session.SteamID + ".maFile");
            string before = File.ReadAllText(maFile);
            SessionData original = SessionPersistenceService.Clone(account.Session);
            SessionData refreshed = TokenSession(SyntheticJwt.Valid(), SyntheticJwt.Valid());
            refreshed.SteamID = account.Session.SteamID;
            File.SetAttributes(maFile, FileAttributes.ReadOnly);
            try
            {
                SessionSaveResult result = new SessionPersistenceService().Commit(account, refreshed, directory, null, true);
                Assert.Equal(SessionSaveStatus.Failed, result.Status);
            }
            finally
            {
                File.SetAttributes(maFile, FileAttributes.Normal);
            }

            Assert.Equal(original.AccessToken, account.Session.AccessToken);
            Assert.Equal(before, File.ReadAllText(maFile));
            Assert.DoesNotContain(refreshed.AccessToken, File.ReadAllText(maFile));
        }

        [Fact]
        public void LoginWindow_TracksIdleLoggingInSuccessFailureAndCancel()
        {
            LoginWindowViewModel viewModel = new LoginWindowViewModel("fixture_user");

            Assert.Equal(LoginUiState.Idle, viewModel.State);
            Assert.Equal("Login", viewModel.LoginButtonText);
            Assert.True(viewModel.CanSubmit);
            Assert.True(viewModel.CanCancel);
            Assert.Equal("fixture_user", viewModel.AccountName);

            viewModel.MarkLoggingIn();
            Assert.Equal(LoginUiState.LoggingIn, viewModel.State);
            Assert.Equal("Logging in...", viewModel.LoginButtonText);
            Assert.False(viewModel.CanSubmit);
            Assert.True(viewModel.CanCancel);

            viewModel.SetStatus("Waiting for Steam authentication...");
            Assert.Equal("Waiting for Steam authentication...", viewModel.StatusText);
            Assert.Equal(LoginUiState.LoggingIn, viewModel.State);

            viewModel.MarkFailed("Steam login failed.");
            Assert.Equal(LoginUiState.Failure, viewModel.State);
            Assert.Equal("Login", viewModel.LoginButtonText);
            Assert.True(viewModel.CanSubmit);

            viewModel.MarkLoggingIn();
            viewModel.MarkSucceeded();
            Assert.Equal(LoginUiState.Success, viewModel.State);
            Assert.Equal("Login successful.", viewModel.StatusText);

            viewModel.MarkCancelled();
            Assert.Equal(LoginUiState.Cancelled, viewModel.State);
        }

        [Fact]
        public async Task ChallengeHandler_DeviceCodeDelegatesAndEmailCancelPropagates()
        {
            int generated = 0;
            LoginChallengeHandler handler = new LoginChallengeHandler(
                new FakeDeviceCodes(() =>
                {
                    generated++;
                    return Task.FromResult("AAAAA");
                }),
                (email, incorrect, token) => Task.FromResult<string>(null),
                _ => { },
                CancellationToken.None,
                token => Task.CompletedTask);

            string code = await handler.GetDeviceCodeAsync(false);
            Assert.Equal("AAAAA", code);
            Assert.Equal(1, generated);
            Assert.False(await handler.AcceptDeviceConfirmationAsync());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.GetEmailCodeAsync("person@example.com", true));
        }

        [Fact]
        public async Task ChallengeHandler_RepeatedDeviceCodeWaitIsCancellable()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            string warning = null;
            LoginChallengeHandler handler = new LoginChallengeHandler(
                new FakeDeviceCodes(() => Task.FromResult("AAAAA")),
                (email, incorrect, token) => Task.FromResult("email-code"),
                message => warning = message,
                cancellation.Token,
                token =>
                {
                    cancellation.Cancel();
                    return Task.Delay(Timeout.Infinite, token);
                });

            await handler.GetDeviceCodeAsync(false);
            await handler.GetDeviceCodeAsync(false);
            await handler.GetDeviceCodeAsync(false);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.GetDeviceCodeAsync(true));
            Assert.Equal("Steam rejected multiple authenticator codes. Check that SDA is still the authenticator for this account.", warning);
        }

        [Fact]
        public async Task ForceRefresh_ExpiredAccessUpdatesSameAccount()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = SyntheticJwt.Expired();
            Manifest.GetManifest(directory).SaveAccount(account, false);
            Harness harness = CreateHarness();
            await harness.ViewModel.LoadDirectoryAsync(directory, false);
            AccountViewModel selected = harness.ViewModel.SelectedAccount;
            string nextAccess = SyntheticJwt.Valid();
            harness.Refresher.OnRefresh = session =>
            {
                session.AccessToken = nextAccess;
                return Task.CompletedTask;
            };

            await harness.ViewModel.ForceRefreshAsync();

            Assert.Equal(1, harness.Refresher.Calls);
            Assert.Same(selected, harness.ViewModel.SelectedAccount);
            Assert.Same(selected.Account.Session, harness.ViewModel.SelectedAccount.Account.Session);
            Assert.Equal(nextAccess, selected.Account.Session.AccessToken);
            Assert.Equal("Session refreshed.", harness.ViewModel.StatusText);
            Assert.Contains(nextAccess, File.ReadAllText(Path.Combine(directory, account.Session.SteamID + ".maFile")));
        }

        [Fact]
        public async Task ForceRefresh_ValidSessionStillRefreshes()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = SyntheticJwt.Valid();
            Manifest.GetManifest(directory).SaveAccount(account, false);
            Harness harness = CreateHarness();
            await harness.ViewModel.LoadDirectoryAsync(directory, false);

            await harness.ViewModel.ForceRefreshAsync();

            Assert.Equal(1, harness.Refresher.Calls);
            Assert.Equal("Session refreshed.", harness.ViewModel.StatusText);
        }

        [Fact]
        public async Task ForceRefresh_MissingOrExpiredRefreshAsksForLoginAgain()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = "";
            Manifest.GetManifest(directory).SaveAccount(account, false);
            Harness harness = CreateHarness();
            await harness.ViewModel.LoadDirectoryAsync(directory, false);

            await harness.ViewModel.ForceRefreshAsync();

            Assert.Equal(0, harness.Refresher.Calls);
            Assert.Equal("Session cannot be refreshed. Use Login Again.", harness.ViewModel.StatusText);
            Assert.True(harness.ViewModel.CanUseSessionActions);

            account.Session.RefreshToken = SyntheticJwt.Expired();
            account.Session.AccessToken = SyntheticJwt.Valid();
            Manifest.GetManifest(directory).SaveAccount(account, false);
            await harness.ViewModel.LoadDirectoryAsync(directory, false);
            await harness.ViewModel.ForceRefreshAsync();

            Assert.Equal(0, harness.Refresher.Calls);
            Assert.Equal("Session cannot be refreshed. Use Login Again.", harness.ViewModel.StatusText);
        }

        [Fact]
        public async Task ForceRefresh_FailureLeavesAccountReadable()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            string previousAccess = SyntheticJwt.Valid();
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = previousAccess;
            Manifest.GetManifest(directory).SaveAccount(account, false);
            Harness harness = CreateHarness();
            await harness.ViewModel.LoadDirectoryAsync(directory, false);
            harness.Refresher.OnRefresh = _ => throw new InvalidOperationException("Failed to refresh token: offline");

            await harness.ViewModel.ForceRefreshAsync();

            Assert.Equal("Unable to refresh session.", harness.ViewModel.StatusText);
            Assert.Equal(previousAccess, harness.ViewModel.SelectedAccount.Account.Session.AccessToken);
            Assert.Equal("PFRRH", harness.ViewModel.CurrentCode);
            Assert.True(harness.ViewModel.CanCopyCode);
        }

        [Fact]
        public async Task LoginAgain_PersistsNewSessionWithoutStoringPassword()
        {
            string directory = NewDirectory();
            string settingsPath = Path.Combine(_root, "settings.json");
            SteamGuardAccount account = LoadPlainAccount();
            Manifest.GetManifest(directory).SaveAccount(account, false);
            Harness harness = CreateHarness(settingsPath);
            await harness.ViewModel.LoadDirectoryAsync(directory, false);
            AccountViewModel selected = harness.ViewModel.SelectedAccount;
            SessionData refreshed = TokenSession(SyntheticJwt.Valid(), SyntheticJwt.Valid());
            refreshed.SteamID = selected.Account.Session.SteamID;
            const string steamPassword = "transient-steam-password";

            string error = await harness.ViewModel.CommitRefreshedSessionAsync(refreshed, harness.Prompt);

            Assert.Null(error);
            Assert.Same(selected, harness.ViewModel.SelectedAccount);
            Assert.Same(refreshed, selected.Account.Session);
            Assert.Equal("Login successful.", harness.ViewModel.StatusText);
            string maFile = File.ReadAllText(Path.Combine(directory, refreshed.SteamID + ".maFile"));
            Assert.Contains(refreshed.AccessToken, maFile);
            Assert.DoesNotContain(steamPassword, maFile);
            Assert.False(File.Exists(settingsPath));
        }

        [Fact]
        public async Task SessionActions_FollowSelectionAndBlockDuplicates()
        {
            Harness harness = CreateHarness();
            Assert.False(harness.ViewModel.CanUseSessionActions);
            Assert.False(harness.ViewModel.TryBeginSessionOperation());

            string directory = NewDirectory();
            Manifest.GetManifest(directory).SaveAccount(LoadPlainAccount(), false);
            await harness.ViewModel.LoadDirectoryAsync(directory, false);
            Assert.True(harness.ViewModel.CanUseSessionActions);
            Assert.True(harness.ViewModel.TryBeginSessionOperation());
            Assert.False(harness.ViewModel.CanUseSessionActions);
            Assert.False(harness.ViewModel.TryBeginSessionOperation());
            harness.ViewModel.EndSessionOperation();
            Assert.True(harness.ViewModel.CanUseSessionActions);
        }

        private Harness CreateHarness()
        {
            return CreateHarness(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json"));
        }

        private Harness CreateHarness(string settingsPath)
        {
            RecordingRefresher refresher = new RecordingRefresher();
            MainWindowViewModel viewModel = new MainWindowViewModel(
                new AccountService(),
                new SettingsService(settingsPath),
                new FixedClock(),
                new ScriptedPrompt(),
                new ScriptedFolders(),
                new RecordingClipboard(),
                NewDirectory(),
                refresher);
            return new Harness
            {
                ViewModel = viewModel,
                Refresher = refresher,
                Prompt = new ScriptedPrompt()
            };
        }

        private static SessionData TokenSession(string refreshToken, string accessToken)
        {
            return new SessionData
            {
                SteamID = 76561198000000001,
                RefreshToken = refreshToken,
                AccessToken = accessToken,
            };
        }

        private static SteamGuardAccount LoadPlainAccount()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fixture-plain-mafile.json");
            return JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(fixture));
        }

        private string NewDirectory()
        {
            return Path.Combine(_root, Guid.NewGuid().ToString("N"));
        }

        private sealed class Harness
        {
            public MainWindowViewModel ViewModel { get; set; }
            public RecordingRefresher Refresher { get; set; }
            public ScriptedPrompt Prompt { get; set; }
        }

        private sealed class RecordingRefresher : IAccessTokenRefresher
        {
            public int Calls;
            public Func<SessionData, Task> OnRefresh;

            public Task RefreshAsync(SessionData session, CancellationToken cancellationToken)
            {
                Calls++;
                cancellationToken.ThrowIfCancellationRequested();
                if (OnRefresh != null)
                {
                    return OnRefresh(session);
                }

                session.AccessToken = SyntheticJwt.Valid();
                return Task.CompletedTask;
            }
        }

        private sealed class FakeDeviceCodes : IDeviceCodeProvider
        {
            private readonly Func<Task<string>> _generate;

            public FakeDeviceCodes(Func<Task<string>> generate)
            {
                _generate = generate;
            }

            public Task<string> GenerateAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _generate();
            }
        }

        private sealed class FixedClock : ISteamClock
        {
            public Task<long> GetSteamTimeAsync()
            {
                return Task.FromResult(1600000000L);
            }
        }

        private sealed class ScriptedPrompt : IEncryptionPrompt
        {
            public Task<string> PromptAsync(string errorMessage)
            {
                return Task.FromResult<string>(null);
            }
        }

        private sealed class ScriptedFolders : IFolderPicker
        {
            public Task<string> PickAsync()
            {
                return Task.FromResult<string>(null);
            }
        }

        private sealed class RecordingClipboard : IClipboardService
        {
            public Task SetTextAsync(string text)
            {
                return Task.CompletedTask;
            }
        }
    }
}
