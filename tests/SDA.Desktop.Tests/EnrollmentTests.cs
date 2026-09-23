using Newtonsoft.Json;
using SDA.Core.Storage;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using SteamKit2.Authentication;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class EnrollmentTests : IDisposable
    {
        private readonly string _root;

        public EnrollmentTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-enroll-").FullName;
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
        public async Task LoginFailure_DoesNotCreateLinkerOrMaFile()
        {
            string directory = NewDirectory();
            RecordingLogin login = new RecordingLogin { Result = SteamLoginResult.Fail("Steam login failed.") };
            RecordingLinkerFactory factory = new RecordingLinkerFactory();
            SetupAccountWindowViewModel vm = CreateVm(directory, login, factory);

            await vm.LoginAsync("transient-steam-password", new LoginChallengeHandler(new NullDeviceCodes(), (_, __, ___) => Task.FromResult<string>(null), null, CancellationToken.None));

            Assert.Equal(EnrollmentUiState.Failed, vm.State);
            Assert.Equal(0, factory.CreateCalls);
            Assert.Null(vm.Linker);
            Assert.False(File.Exists(Path.Combine(directory, "manifest.json")));
        }

        [Fact]
        public async Task LoginCancellation_DoesNotCreateLinkerOrMaFile()
        {
            string directory = NewDirectory();
            RecordingLogin login = new RecordingLogin { Result = SteamLoginResult.Cancel() };
            RecordingLinkerFactory factory = new RecordingLinkerFactory();
            SetupAccountWindowViewModel vm = CreateVm(directory, login, factory);

            await vm.LoginAsync("transient-steam-password", NullChallenges());

            Assert.Equal(EnrollmentUiState.Cancelled, vm.State);
            Assert.Equal(0, factory.CreateCalls);
            Assert.False(Directory.Exists(directory) && File.Exists(Path.Combine(directory, "manifest.json")));
        }

        [Fact]
        public async Task LoginSuccess_ThenDecline_DoesNotCreateLinkerOrMaFile()
        {
            string directory = NewDirectory();
            RecordingLogin login = SuccessfulLogin();
            RecordingLinkerFactory factory = new RecordingLinkerFactory();
            SetupAccountWindowViewModel vm = CreateVm(directory, login, factory);
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            Assert.Equal(EnrollmentUiState.ConfirmContinue, vm.State);

            vm.DeclineContinue();

            Assert.Equal(EnrollmentUiState.Cancelled, vm.State);
            Assert.Equal(0, factory.CreateCalls);
            Assert.False(File.Exists(Path.Combine(directory, "manifest.json")));
        }

        [Fact]
        public async Task PhoneRequired_PassesValuesAndRetriesAddAuthenticator()
        {
            RecordingLinker linker = new RecordingLinker();
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.MustProvidePhoneNumber);
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.AwaitingFinalization);
            linker.Linked = SyntheticLinkedAccount();
            SetupAccountWindowViewModel vm = CreateVm(NewDirectory(), SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            Assert.Equal(EnrollmentUiState.NeedPhoneNumber, vm.State);
            Assert.Equal(1, linker.AddCalls);

            await vm.SubmitPhoneAsync("+61 400000000", "au");
            Assert.Equal("+61 400000000", linker.PhoneNumber);
            Assert.Equal("AU", linker.PhoneCountryCode);
            Assert.Equal(2, linker.AddCalls);
            Assert.Equal(EnrollmentUiState.NeedNewEncryptionPasskey, vm.State);
        }

        [Fact]
        public async Task InvalidPhone_DoesNotRetryAddAuthenticator()
        {
            RecordingLinker linker = new RecordingLinker();
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.MustProvidePhoneNumber);
            SetupAccountWindowViewModel vm = CreateVm(NewDirectory(), SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            await vm.SubmitPhoneAsync("61400000000", "AU");

            Assert.Equal(EnrollmentUiState.NeedPhoneNumber, vm.State);
            Assert.Equal(1, linker.AddCalls);
            Assert.Null(linker.PhoneNumber);
            Assert.Equal("Phone number must start with + and country code.", vm.PhoneError);
        }

        [Fact]
        public async Task EmailConfirmation_CallsAddAuthenticatorAgain()
        {
            RecordingLinker linker = new RecordingLinker { ConfirmationEmailAddress = "f***@example.com" };
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.MustConfirmEmail);
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.AwaitingFinalization);
            linker.Linked = SyntheticLinkedAccount();
            SetupAccountWindowViewModel vm = CreateVm(NewDirectory(), SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            Assert.Equal(EnrollmentUiState.NeedEmailConfirmation, vm.State);
            Assert.Contains("f***@example.com", vm.EmailPrompt);

            await vm.ConfirmEmailAsync();
            Assert.Equal(2, linker.AddCalls);
        }

        [Fact]
        public async Task AuthenticatorPresent_StopsWithoutMaFile()
        {
            string directory = NewDirectory();
            RecordingLinker linker = new RecordingLinker();
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.AuthenticatorPresent);
            SetupAccountWindowViewModel vm = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();

            Assert.Equal(EnrollmentUiState.Failed, vm.State);
            Assert.Equal(AuthenticatorEnrollmentService.AuthenticatorPresentMessage, vm.StatusText);
            Assert.False(File.Exists(Path.Combine(directory, "76561198000000099.maFile")));
        }

        [Fact]
        public async Task FailureAddingPhone_ReturnsToPhoneEntry()
        {
            RecordingLinker linker = new RecordingLinker();
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.MustProvidePhoneNumber);
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.FailureAddingPhone);
            SetupAccountWindowViewModel vm = CreateVm(NewDirectory(), SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            await vm.SubmitPhoneAsync("+61 400000000", "AU");

            Assert.Equal(EnrollmentUiState.NeedPhoneNumber, vm.State);
            Assert.Null(linker.PhoneNumber);
            Assert.Equal(AuthenticatorEnrollmentService.PhoneAddFailedMessage, vm.PhoneError);
        }

        [Fact]
        public async Task InitialStorage_EmptyPlainEncryptedAndExistingManifests()
        {
            string emptyPlain = NewDirectory();
            await EnrollThroughSave(emptyPlain, null, linker => { }, vm => vm.SubmitNewPasskeyAsync(null));
            Assert.StartsWith("{", File.ReadAllText(Path.Combine(emptyPlain, "76561198000000099.maFile")).TrimStart());

            string emptyEncrypted = NewDirectory();
            await EnrollThroughSave(emptyEncrypted, null, linker => { }, vm => vm.SubmitNewPasskeyAsync("fixture-enroll-pass"));
            string encryptedFile = File.ReadAllText(Path.Combine(emptyEncrypted, "76561198000000099.maFile"));
            Assert.False(encryptedFile.TrimStart().StartsWith("{"));
            Assert.DoesNotContain("c3ludGhldGljLWVucm9sbC1zaGFyZWQ=", encryptedFile);
            SteamGuardAccount reloaded = Manifest.GetManifest(emptyEncrypted).LoadAccounts("fixture-enroll-pass").Accounts[0];
            Assert.Equal("fixture_new", reloaded.AccountName);
        }

        [Fact]
        public async Task InitialStorage_ExistingUnencryptedAndEncryptedManifests()
        {
            string plainDir = NewDirectory();
            SteamGuardAccount existing = LoadPlainAccount();
            Manifest.GenerateNewManifest(plainDir, false).SaveAccount(existing, false);
            await EnrollThroughSave(plainDir, null, linker => { }, vm => Task.CompletedTask);
            Assert.Equal(2, Manifest.GetManifest(plainDir).Entries.Count);

            string encryptedDir = NewDirectory();
            SteamGuardAccount encryptedExisting = LoadPlainAccount();
            Assert.True(Manifest.GenerateNewManifest(encryptedDir, false).SaveAccount(encryptedExisting, true, "fixture-pass"));
            await EnrollThroughSave(encryptedDir, "fixture-pass", linker => { }, vm => Task.CompletedTask);
            string stored = File.ReadAllText(Path.Combine(encryptedDir, "76561198000000099.maFile"));
            Assert.False(stored.TrimStart().StartsWith("{"));
            Assert.Equal(2, Manifest.GetManifest(encryptedDir).Entries.Count);
        }

        [Fact]
        public async Task ExistingEncrypted_WrongPasskeyDoesNotSave()
        {
            string directory = NewDirectory();
            SteamGuardAccount existing = LoadPlainAccount();
            Assert.True(Manifest.GenerateNewManifest(directory, false).SaveAccount(existing, true, "fixture-pass"));
            RecordingLinker linker = AwaitingLinker();
            SetupAccountWindowViewModel vm = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker), null);
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            Assert.Equal(EnrollmentUiState.NeedExistingEncryptionPasskey, vm.State);
            await vm.SubmitExistingPasskeyAsync("wrong-passkey");
            Assert.Equal(EnrollmentUiState.NeedExistingEncryptionPasskey, vm.State);
            Assert.False(File.Exists(Path.Combine(directory, "76561198000000099.maFile")));
            Assert.Equal(0, linker.FinalizeCalls);
        }

        [Fact]
        public async Task SaveAccount_HappensBeforeFinalize()
        {
            string directory = NewDirectory();
            RecordingLinker linker = AwaitingLinker();
            SetupAccountWindowViewModel vm = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            await vm.SubmitNewPasskeyAsync(null);

            Assert.True(vm.SavedInitially);
            Assert.True(File.Exists(Path.Combine(directory, "76561198000000099.maFile")));
            Assert.Equal(0, linker.FinalizeCalls);

            vm.ContinueRevocationDisplay();
            await vm.SubmitRevocationConfirmationAsync("R00000");
            linker.FinalizeResult = AuthenticatorLinker.FinalizeResult.Success;
            await vm.SubmitSmsAsync("12345");

            Assert.Equal(1, linker.FinalizeCalls);
            Assert.True(linker.SavedBeforeFinalize);
        }

        [Fact]
        public async Task InitialSaveFailure_DoesNotFinalize()
        {
            RecordingLinker linker = AwaitingLinker();
            FailingPersistence persistence = new FailingPersistence { FailSave = true };
            SetupAccountWindowViewModel vm = new SetupAccountWindowViewModel(
                SuccessfulLogin(),
                new RecordingLinkerFactory(linker),
                persistence,
                NewDirectory(),
                null);
            vm.Username = "fixture_new";

            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            await vm.SubmitNewPasskeyAsync(null);

            Assert.Equal(EnrollmentUiState.Failed, vm.State);
            Assert.Equal(0, linker.FinalizeCalls);
            Assert.False(vm.SavedInitially);
        }

        [Fact]
        public async Task RevocationConfirmation_CorrectProceedsIncorrectDoesNotFinalize()
        {
            string directory = NewDirectory();
            RecordingLinker linker = AwaitingLinker();
            SetupAccountWindowViewModel vm = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";
            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            await vm.SubmitNewPasskeyAsync(null);
            vm.ContinueRevocationDisplay();
            await vm.SubmitRevocationConfirmationAsync("wrong");
            Assert.Equal(EnrollmentUiState.Failed, vm.State);
            Assert.Equal(0, linker.FinalizeCalls);
            Assert.False(File.Exists(Path.Combine(directory, "76561198000000099.maFile")));

            RecordingLinker second = AwaitingLinker();
            string secondDir = NewDirectory();
            SetupAccountWindowViewModel ok = CreateVm(secondDir, SuccessfulLogin(), new RecordingLinkerFactory(second));
            ok.Username = "fixture_new";
            await ok.LoginAsync("transient-steam-password", NullChallenges());
            await ok.ConfirmContinueAsync();
            await ok.SubmitNewPasskeyAsync(null);
            ok.ContinueRevocationDisplay();
            await ok.SubmitRevocationConfirmationAsync("r00000");
            Assert.Equal(EnrollmentUiState.NeedSmsCode, ok.State);
            Assert.Equal(0, second.FinalizeCalls);
        }

        [Fact]
        public async Task BadSmsCode_AsksAgainAndReusesLinker()
        {
            string directory = NewDirectory();
            RecordingLinker linker = AwaitingLinker();
            linker.FinalizeQueue.Enqueue(AuthenticatorLinker.FinalizeResult.BadSMSCode);
            linker.FinalizeQueue.Enqueue(AuthenticatorLinker.FinalizeResult.Success);
            SetupAccountWindowViewModel vm = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";
            await CompleteToSms(vm);

            await vm.SubmitSmsAsync("00000");
            Assert.Equal(EnrollmentUiState.NeedSmsCode, vm.State);
            Assert.Same(linker, vm.Linker);

            await vm.SubmitSmsAsync("12345");
            Assert.Equal(EnrollmentUiState.Completed, vm.State);
            Assert.True(linker.Linked.FullyEnrolled);
            Assert.Equal(2, linker.FinalizeCalls);
        }

        [Fact]
        public async Task FinalizationSuccess_SavesFullyEnrolledAccount()
        {
            string directory = NewDirectory();
            RecordingLinker linker = AwaitingLinker();
            linker.FinalizeResult = AuthenticatorLinker.FinalizeResult.Success;
            SetupAccountWindowViewModel vm = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";
            await CompleteToSms(vm);
            await vm.SubmitSmsAsync("12345");

            Assert.Equal(EnrollmentUiState.Completed, vm.State);
            SteamGuardAccount reloaded = Manifest.GetManifest(directory).LoadAccounts(null).Accounts[0];
            Assert.True(reloaded.FullyEnrolled);
            Assert.Equal("fixture_new", reloaded.AccountName);
            Assert.False(string.IsNullOrEmpty(reloaded.SharedSecret));
            Assert.NotNull(reloaded.Session);
        }

        [Fact]
        public async Task FinalizationGeneralFailure_KeepsRevocationAndIsNotCompleted()
        {
            string directory = NewDirectory();
            RecordingLinker linker = AwaitingLinker();
            linker.FinalizeResult = AuthenticatorLinker.FinalizeResult.GeneralFailure;
            SetupAccountWindowViewModel vm = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker));
            vm.Username = "fixture_new";
            await CompleteToSms(vm);
            await vm.SubmitSmsAsync("12345");

            Assert.Equal(EnrollmentUiState.Failed, vm.State);
            Assert.Equal("R00000", vm.RevocationCode);
            Assert.False(File.Exists(Path.Combine(directory, "76561198000000099.maFile")));
        }

        [Fact]
        public async Task FinalSaveFailure_PreservesPreFinalizationMaFile()
        {
            string directory = NewDirectory();
            RecordingLinker linker = AwaitingLinker();
            linker.FinalizeResult = AuthenticatorLinker.FinalizeResult.Success;
            EnrollmentPersistenceService real = new EnrollmentPersistenceService();
            FinalSaveFailPersistence persistence = new FinalSaveFailPersistence(real, directory);
            SetupAccountWindowViewModel vm = new SetupAccountWindowViewModel(
                SuccessfulLogin(),
                new RecordingLinkerFactory(linker),
                persistence,
                directory,
                null);
            vm.Username = "fixture_new";
            await CompleteToSms(vm);
            string before = File.ReadAllText(Path.Combine(directory, "76561198000000099.maFile"));
            await vm.SubmitSmsAsync("12345");

            Assert.Equal(EnrollmentUiState.Failed, vm.State);
            Assert.Equal(AuthenticatorEnrollmentService.FinalSaveFailedMessage, vm.StatusText);
            Assert.Equal("R00000", vm.RevocationCode);
            Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "76561198000000099.maFile")));
            SteamGuardAccount stored = JsonConvert.DeserializeObject<SteamGuardAccount>(before);
            Assert.False(stored.FullyEnrolled);
        }

        [Fact]
        public async Task ChallengeHandler_NullAccountDoesNotInventDeviceCode()
        {
            LoginChallengeHandler handler = new LoginChallengeHandler(
                new SteamGuardDeviceCodeProvider(null),
                (_, __, ___) => Task.FromResult("unused"),
                null,
                CancellationToken.None);
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.GetDeviceCodeAsync(false));
            Assert.Equal(LoginChallengeHandler.ExistingAuthenticatorRequiredMessage, error.Message);
        }

        [Fact]
        public async Task MainWindow_ReloadsAndSelectsEnrolledAccount()
        {
            string directory = NewDirectory();
            RecordingLinker linker = AwaitingLinker();
            linker.FinalizeResult = AuthenticatorLinker.FinalizeResult.Success;
            SetupAccountWindowViewModel setup = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker));
            setup.Username = "fixture_new";
            await CompleteToSms(setup);
            await setup.SubmitSmsAsync("12345");

            MainWindowViewModel main = new MainWindowViewModel(
                new AccountService(),
                new SettingsService(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json")),
                new FixedClock(),
                new SilentPrompt(),
                new SilentFolders(),
                new SilentClipboard(),
                directory);
            await main.LoadDirectoryAsync(directory, false);
            await main.ReloadAfterEnrollmentAsync("fixture_new", null);
            Assert.Equal("fixture_new", main.SelectedAccount.Account.AccountName);
            Assert.True(main.SelectedAccount.Account.FullyEnrolled);
        }

        [Fact]
        public void RevocationMatches_IsCaseInsensitiveOnBothSides()
        {
            Assert.True(AuthenticatorEnrollmentService.RevocationMatches("r00000", "R00000"));
            Assert.True(AuthenticatorEnrollmentService.RevocationMatches("R00000", "r00000"));
            Assert.False(AuthenticatorEnrollmentService.RevocationMatches("R00001", "R00000"));
            Assert.False(AuthenticatorEnrollmentService.RevocationMatches("R00000", null));
            Assert.False(AuthenticatorEnrollmentService.RevocationMatches("R00000", ""));
        }

        [Fact]
        public void ValidatePhone_ReportsTheFailingField()
        {
            Assert.Equal(PhoneValidationField.Phone, AuthenticatorEnrollmentService.ValidatePhone("0412345678", "AU").Field);
            Assert.Equal(PhoneValidationField.Country, AuthenticatorEnrollmentService.ValidatePhone("+61412345678", "A1").Field);
            Assert.Equal(PhoneValidationField.None, AuthenticatorEnrollmentService.ValidatePhone("+61412345678", "AU").Field);
        }

        private async Task EnrollThroughSave(string directory, string currentKey, Action<RecordingLinker> configure, Func<SetupAccountWindowViewModel, Task> afterAdd)
        {
            RecordingLinker linker = AwaitingLinker();
            configure(linker);
            SetupAccountWindowViewModel vm = CreateVm(directory, SuccessfulLogin(), new RecordingLinkerFactory(linker), currentKey);
            vm.Username = "fixture_new";
            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            await afterAdd(vm);
            if (vm.State == EnrollmentUiState.NeedNewEncryptionPasskey)
            {
                await vm.SubmitNewPasskeyAsync(null);
            }
        }

        private static async Task CompleteToSms(SetupAccountWindowViewModel vm)
        {
            await vm.LoginAsync("transient-steam-password", NullChallenges());
            await vm.ConfirmContinueAsync();
            if (vm.State == EnrollmentUiState.NeedNewEncryptionPasskey)
            {
                await vm.SubmitNewPasskeyAsync(null);
            }

            vm.ContinueRevocationDisplay();
            await vm.SubmitRevocationConfirmationAsync("R00000");
        }

        private SetupAccountWindowViewModel CreateVm(string directory, ISteamLoginService login, IAuthenticatorLinkerFactory factory, string passKey = null)
        {
            RecordingLinkerFactory recording = factory as RecordingLinkerFactory;
            if (recording != null && recording.Linker != null)
            {
                recording.Linker.Directory = directory;
            }

            return new SetupAccountWindowViewModel(login, factory, new EnrollmentPersistenceService(), directory, passKey);
        }

        private static RecordingLogin SuccessfulLogin()
        {
            return new RecordingLogin
            {
                Result = SteamLoginResult.Success(new SessionData
                {
                    SteamID = 76561198000000099,
                    AccessToken = SyntheticJwt.Valid(),
                    RefreshToken = SyntheticJwt.Valid()
                })
            };
        }

        private static RecordingLinker AwaitingLinker()
        {
            RecordingLinker linker = new RecordingLinker();
            linker.AddResults.Enqueue(AuthenticatorLinker.LinkResult.AwaitingFinalization);
            linker.Linked = SyntheticLinkedAccount();
            return linker;
        }

        private static SteamGuardAccount SyntheticLinkedAccount()
        {
            return new SteamGuardAccount
            {
                AccountName = "fixture_new",
                SharedSecret = "c3ludGhldGljLWVucm9sbC1zaGFyZWQ=",
                IdentitySecret = "c3ludGhldGljLWVucm9sbC1pZGVudA==",
                RevocationCode = "R00000",
                DeviceID = "android:00000000-0000-0000-0000-000000000099",
                FullyEnrolled = false,
                Session = new SessionData
                {
                    SteamID = 76561198000000099,
                    AccessToken = SyntheticJwt.Valid(),
                    RefreshToken = SyntheticJwt.Valid()
                }
            };
        }

        private static SteamGuardAccount LoadPlainAccount()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fixture-plain-mafile.json");
            return JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(fixture));
        }

        private static IAuthenticator NullChallenges()
        {
            return new LoginChallengeHandler(new NullDeviceCodes(), (_, __, ___) => Task.FromResult("email"), null, CancellationToken.None);
        }

        private string NewDirectory()
        {
            return Path.Combine(_root, Guid.NewGuid().ToString("N"));
        }

        private sealed class RecordingLogin : ISteamLoginService
        {
            public SteamLoginResult Result;

            public Task<SteamLoginResult> LoginAgainAsync(SteamGuardAccount account, string password, IAuthenticator challenges, ILoginStatus progress, CancellationToken cancellationToken)
            {
                return AuthenticateCredentialsAsync(account == null ? null : account.AccountName, password, challenges, progress, cancellationToken);
            }

            public Task<SteamLoginResult> AuthenticateCredentialsAsync(string username, string password, IAuthenticator challenges, ILoginStatus progress, CancellationToken cancellationToken)
            {
                return Task.FromResult(Result);
            }
        }

        private sealed class RecordingLinkerFactory : IAuthenticatorLinkerFactory
        {
            public readonly RecordingLinker Linker;
            public int CreateCalls;

            public RecordingLinkerFactory()
                : this(new RecordingLinker())
            {
            }

            public RecordingLinkerFactory(RecordingLinker linker)
            {
                Linker = linker;
            }

            public IAuthenticatorLinker Create(SessionData session)
            {
                CreateCalls++;
                return Linker;
            }
        }

        private sealed class RecordingLinker : IAuthenticatorLinker
        {
            public readonly Queue<AuthenticatorLinker.LinkResult> AddResults = new Queue<AuthenticatorLinker.LinkResult>();
            public readonly Queue<AuthenticatorLinker.FinalizeResult> FinalizeQueue = new Queue<AuthenticatorLinker.FinalizeResult>();
            public int AddCalls;
            public int FinalizeCalls;
            public AuthenticatorLinker.FinalizeResult FinalizeResult = AuthenticatorLinker.FinalizeResult.Success;
            public SteamGuardAccount Linked;
            public bool SavedBeforeFinalize;
            public string Directory;

            public string PhoneNumber { get; set; }
            public string PhoneCountryCode { get; set; }
            public string ConfirmationEmailAddress { get; set; }
            public SteamGuardAccount LinkedAccount { get { return Linked; } }

            public Task<AuthenticatorLinker.LinkResult> AddAuthenticatorAsync()
            {
                AddCalls++;
                return Task.FromResult(AddResults.Count == 0 ? AuthenticatorLinker.LinkResult.GeneralFailure : AddResults.Dequeue());
            }

            public Task<AuthenticatorLinker.FinalizeResult> FinalizeAddAuthenticatorAsync(string smsCode)
            {
                FinalizeCalls++;
                if (!string.IsNullOrEmpty(Directory) && Linked != null && Linked.Session != null)
                {
                    SavedBeforeFinalize = File.Exists(Path.Combine(Directory, Linked.Session.SteamID + ".maFile"));
                }

                AuthenticatorLinker.FinalizeResult result = FinalizeQueue.Count > 0 ? FinalizeQueue.Dequeue() : FinalizeResult;
                if (result == AuthenticatorLinker.FinalizeResult.Success && Linked != null)
                {
                    Linked.FullyEnrolled = true;
                }

                return Task.FromResult(result);
            }
        }

        private sealed class FailingPersistence : EnrollmentPersistenceService
        {
            public bool FailSave;

            public override EnrollmentSaveResult SaveAccount(SteamGuardAccount account, string directory, bool encrypt, string passKey)
            {
                if (FailSave)
                {
                    return EnrollmentSaveResult.Failed();
                }

                return base.SaveAccount(account, directory, encrypt, passKey);
            }
        }

        private sealed class FinalSaveFailPersistence : EnrollmentPersistenceService
        {
            private readonly EnrollmentPersistenceService _inner;
            private int _saves;

            public FinalSaveFailPersistence(EnrollmentPersistenceService inner, string directory)
            {
                _inner = inner;
            }

            public override EnrollmentSaveResult SaveAccount(SteamGuardAccount account, string directory, bool encrypt, string passKey)
            {
                _saves++;
                if (_saves > 1)
                {
                    return EnrollmentSaveResult.Failed();
                }

                return _inner.SaveAccount(account, directory, encrypt, passKey);
            }

            public override bool RemoveAccount(SteamGuardAccount account, string directory)
            {
                return _inner.RemoveAccount(account, directory);
            }

            public override EnrollmentEncryptionPlan Inspect(string directory, string currentPassKey)
            {
                return _inner.Inspect(directory, currentPassKey);
            }
        }

        private sealed class NullDeviceCodes : IDeviceCodeProvider
        {
            public Task<string> GenerateAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult("AAAAA");
            }
        }

        private sealed class FixedClock : ISteamClock
        {
            public Task<long> GetSteamTimeAsync()
            {
                return Task.FromResult(1600000000L);
            }
        }

        private sealed class SilentPrompt : IEncryptionPrompt
        {
            public Task<string> PromptAsync(string errorMessage)
            {
                return Task.FromResult<string>(null);
            }
        }

        private sealed class SilentFolders : IFolderPicker
        {
            public Task<string> PickAsync()
            {
                return Task.FromResult<string>(null);
            }
        }

        private sealed class SilentClipboard : IClipboardService
        {
            public Task SetTextAsync(string text)
            {
                return Task.CompletedTask;
            }
        }
    }
}
