using Newtonsoft.Json;
using SDA.Core.Storage;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class ConfirmationTests : IDisposable
    {
        private readonly string _root;

        public ConfirmationTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-confirm-").FullName;
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
        public async Task Load_MissingSessionRequiresLogin()
        {
            SteamGuardAccount account = LoadPlainAccount();
            account.Session = null;
            RecordingClient client = new RecordingClient();
            ConfirmationLoadResult result = await CreateService(client).LoadAsync(account, PersistOk, CancellationToken.None);

            Assert.Equal(ConfirmationLoadStatus.SessionExpired, result.Status);
            Assert.Equal(ConfirmationService.SessionExpiredMessage, result.Message);
            Assert.Equal(0, client.FetchCalls);
        }

        [Fact]
        public async Task Load_ExpiredRefreshTokenRequiresLogin()
        {
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = SyntheticJwt.Expired();
            account.Session.AccessToken = SyntheticJwt.Valid();
            RecordingClient client = new RecordingClient();
            RecordingRefresher refresher = new RecordingRefresher();
            ConfirmationLoadResult result = await CreateService(client, refresher).LoadAsync(account, PersistOk, CancellationToken.None);

            Assert.Equal(ConfirmationLoadStatus.SessionExpired, result.Status);
            Assert.Equal(0, client.FetchCalls);
            Assert.Equal(0, refresher.Calls);
        }

        [Fact]
        public async Task Load_ExpiredAccessRefreshesBeforeFetch()
        {
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = SyntheticJwt.Expired();
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { TradeConfirmation() } };
            RecordingRefresher refresher = new RecordingRefresher();
            ConfirmationLoadResult result = await CreateService(client, refresher).LoadAsync(account, PersistOk, CancellationToken.None);

            Assert.Equal(ConfirmationLoadStatus.Loaded, result.Status);
            Assert.True(result.SessionWasRefreshed);
            Assert.Equal(1, refresher.Calls);
            Assert.Equal(1, client.FetchCalls);
            Assert.Single(result.Confirmations);
        }

        [Fact]
        public async Task Load_ValidAccessFetchesDirectly()
        {
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = SyntheticJwt.Valid();
            RecordingClient client = new RecordingClient { Next = new Confirmation[0] };
            RecordingRefresher refresher = new RecordingRefresher();
            ConfirmationLoadResult result = await CreateService(client, refresher).LoadAsync(account, PersistOk, CancellationToken.None);

            Assert.Equal(ConfirmationLoadStatus.Empty, result.Status);
            Assert.Equal(ConfirmationService.EmptyMessage, result.Message);
            Assert.Equal(0, refresher.Calls);
            Assert.Equal(1, client.FetchCalls);
            Assert.False(result.SessionWasRefreshed);
        }

        [Fact]
        public async Task Load_NeedsAuthenticationWithValidRefreshAsksForForceRefresh()
        {
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = SyntheticJwt.Valid();
            RecordingClient client = new RecordingClient { FetchError = new Exception("Needs Authentication") };
            ConfirmationLoadResult result = await CreateService(client).LoadAsync(account, PersistOk, CancellationToken.None);

            Assert.Equal(ConfirmationLoadStatus.Failed, result.Status);
            Assert.Equal(ConfirmationService.ForceRefreshMessage, result.Message);
        }

        [Fact]
        public async Task Load_RefreshThenPersistUpdatesPlainAndEncryptedAccounts()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = SyntheticJwt.Expired();
            Manifest.GetManifest(directory).SaveAccount(account, false);
            string nextAccess = SyntheticJwt.Valid();
            RecordingRefresher refresher = new RecordingRefresher
            {
                OnRefresh = session =>
                {
                    session.AccessToken = nextAccess;
                    return Task.CompletedTask;
                }
            };
            RecordingClient client = new RecordingClient { Next = new Confirmation[0] };
            SessionPersistenceService persistence = new SessionPersistenceService();

            ConfirmationLoadResult result = await CreateService(client, refresher).LoadAsync(
                account,
                (target, session) => Task.FromResult(persistence.Commit(target, session, directory, null, false).Status == SessionSaveStatus.Saved ? null : "Unable to save refreshed session."),
                CancellationToken.None);

            Assert.Equal(ConfirmationLoadStatus.Empty, result.Status);
            Assert.Contains(nextAccess, File.ReadAllText(Path.Combine(directory, account.Session.SteamID + ".maFile")));

            string encryptedDir = NewDirectory();
            SteamGuardAccount encrypted = LoadPlainAccount();
            encrypted.Session.RefreshToken = SyntheticJwt.Valid();
            encrypted.Session.AccessToken = SyntheticJwt.Expired();
            Assert.True(Manifest.GetManifest(encryptedDir).SaveAccount(encrypted, true, "fixture-pass"));
            string maFile = Path.Combine(encryptedDir, encrypted.Session.SteamID + ".maFile");
            string nextEncryptedAccess = SyntheticJwt.Valid();
            RecordingRefresher encryptedRefresher = new RecordingRefresher
            {
                OnRefresh = session =>
                {
                    session.AccessToken = nextEncryptedAccess;
                    return Task.CompletedTask;
                }
            };

            ConfirmationLoadResult encryptedResult = await CreateService(new RecordingClient { Next = new Confirmation[0] }, encryptedRefresher).LoadAsync(
                encrypted,
                (target, session) => Task.FromResult(persistence.Commit(target, session, encryptedDir, "fixture-pass", false).Status == SessionSaveStatus.Saved ? null : "Unable to save refreshed session."),
                CancellationToken.None);

            string stored = File.ReadAllText(maFile);
            Assert.Equal(ConfirmationLoadStatus.Empty, encryptedResult.Status);
            Assert.False(stored.TrimStart().StartsWith("{"));
            Assert.DoesNotContain(nextEncryptedAccess, stored);
            SteamGuardAccount reloaded = Manifest.GetManifest(encryptedDir).LoadAccounts("fixture-pass").Accounts[0];
            Assert.Equal(nextEncryptedAccess, reloaded.Session.AccessToken);
        }

        [Fact]
        public async Task Window_LoadingToLoadedEmptyExpiredAndFailed()
        {
            SteamGuardAccount account = AccountWithValidSession();
            RecordingClient loaded = new RecordingClient { Next = new Confirmation[] { TradeConfirmation() } };
            ConfirmationsWindowViewModel loadedVm = CreateWindow(account, loaded);
            Assert.Equal(ConfirmationUiState.Loading, loadedVm.State);
            await loadedVm.RefreshAsync();
            Assert.Equal(ConfirmationUiState.Loaded, loadedVm.State);
            Assert.Single(loadedVm.Confirmations);
            Assert.Equal("Trade with Fixture", loadedVm.Confirmations[0].Headline);
            Assert.Equal("76561198000000002", loadedVm.Confirmations[0].CreatorText);
            Assert.Equal("You will give Fixture Item\nYou will receive Fixture Item", loadedVm.Confirmations[0].SummaryText);
            Assert.Equal("Accept Trade", loadedVm.Confirmations[0].AcceptLabel);
            Assert.Equal("Cancel Trade", loadedVm.Confirmations[0].CancelLabel);
            Assert.Equal("Refresh", loadedVm.RefreshButtonText);
            Assert.True(loadedVm.CanRefresh);

            ConfirmationsWindowViewModel emptyVm = CreateWindow(account, new RecordingClient { Next = new Confirmation[0] });
            await emptyVm.RefreshAsync();
            Assert.Equal(ConfirmationUiState.Empty, emptyVm.State);
            Assert.Equal(ConfirmationService.EmptyMessage, emptyVm.StatusText);
            Assert.Empty(emptyVm.Confirmations);

            SteamGuardAccount expired = LoadPlainAccount();
            expired.Session.RefreshToken = "";
            ConfirmationsWindowViewModel expiredVm = CreateWindow(expired, new RecordingClient());
            await expiredVm.RefreshAsync();
            Assert.Equal(ConfirmationUiState.SessionExpired, expiredVm.State);
            Assert.Equal(ConfirmationService.SessionExpiredMessage, expiredVm.StatusText);

            ConfirmationsWindowViewModel failedVm = CreateWindow(account, new RecordingClient { FetchError = new InvalidOperationException("offline") });
            await failedVm.RefreshAsync();
            Assert.Equal(ConfirmationUiState.Failed, failedVm.State);
            Assert.Equal("Unable to load confirmations.", failedVm.StatusText);
        }

        [Fact]
        public async Task Window_ManualRefreshReplacesListAndIgnoresDuplicate()
        {
            SteamGuardAccount account = AccountWithValidSession();
            TaskCompletionSource<Confirmation[]> first = new TaskCompletionSource<Confirmation[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            RecordingClient client = new RecordingClient { PendingFetch = first.Task };
            ConfirmationsWindowViewModel vm = CreateWindow(account, client);

            Task initial = vm.RefreshAsync();
            await WaitUntil(() => client.FetchCalls == 1);
            Task duplicate = vm.RefreshAsync();
            Assert.True(vm.IsRefreshing);
            Assert.False(vm.CanRefresh);
            Assert.Equal("Refreshing...", vm.RefreshButtonText);
            Assert.Equal(1, client.FetchCalls);

            first.SetResult(new Confirmation[] { TradeConfirmation(1, "First") });
            await initial;
            await duplicate;
            Assert.Equal("First", vm.Confirmations[0].Headline);

            client.PendingFetch = null;
            client.Next = new Confirmation[] { TradeConfirmation(2, "Second") };
            await vm.RefreshAsync();
            Assert.Single(vm.Confirmations);
            Assert.Equal("Second", vm.Confirmations[0].Headline);
            Assert.Equal(2, client.FetchCalls);
        }

        [Fact]
        public async Task Window_AcceptAndDenyReloadOnSuccessAndKeepItemOnFailure()
        {
            SteamGuardAccount account = AccountWithValidSession();
            Confirmation first = TradeConfirmation(1, "Open trade");
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { first } };
            ConfirmationsWindowViewModel vm = CreateWindow(account, client);
            await vm.RefreshAsync();
            ConfirmationViewModel item = vm.Confirmations[0];

            client.AcceptResult = false;
            await vm.AcceptAsync(item);
            Assert.Equal(1, client.AcceptCalls);
            Assert.Equal(1, client.FetchCalls);
            Assert.Same(item, vm.Confirmations[0]);
            Assert.Equal("Unable to accept confirmation.", vm.StatusText);
            Assert.True(item.CanAct);

            client.DenyResult = false;
            await vm.DenyAsync(item);
            Assert.Equal(1, client.DenyCalls);
            Assert.Equal(1, client.FetchCalls);
            Assert.Equal("Unable to cancel confirmation.", vm.StatusText);
            Assert.Same(item, vm.Confirmations[0]);

            client.AcceptResult = true;
            client.Next = new Confirmation[0];
            await vm.AcceptAsync(item);
            Assert.Equal(2, client.AcceptCalls);
            Assert.Equal(2, client.FetchCalls);
            Assert.Equal(ConfirmationUiState.Empty, vm.State);
        }

        [Fact]
        public async Task Window_DuplicateActionIsBlocked()
        {
            SteamGuardAccount account = AccountWithValidSession();
            TaskCompletionSource<bool> pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RecordingClient client = new RecordingClient
            {
                Next = new Confirmation[] { TradeConfirmation() },
                PendingAccept = pending.Task
            };
            ConfirmationsWindowViewModel vm = CreateWindow(account, client);
            await vm.RefreshAsync();
            ConfirmationViewModel item = vm.Confirmations[0];

            Task first = vm.AcceptAsync(item);
            await WaitUntil(() => client.AcceptCalls == 1);
            Assert.False(item.CanAct);
            Task second = vm.AcceptAsync(item);
            await second;
            Assert.Equal(1, client.AcceptCalls);
            pending.SetResult(true);
            client.Next = new Confirmation[0];
            await first;
            Assert.Equal(1, client.AcceptCalls);
        }

        [Fact]
        public async Task Window_IconFailureLeavesRowUsable()
        {
            SteamGuardAccount account = AccountWithValidSession();
            Confirmation confirmation = TradeConfirmation();
            confirmation.Icon = "https://example.invalid/icon.png";
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { confirmation } };
            ConfirmationsWindowViewModel vm = new ConfirmationsWindowViewModel(
                account,
                CreateService(client),
                PersistOk,
                new FailingIconLoader());

            await vm.RefreshAsync();

            Assert.Equal(ConfirmationUiState.Loaded, vm.State);
            Assert.Equal("Trade with Fixture", vm.Confirmations[0].Headline);
            Assert.False(vm.Confirmations[0].HasIcon);
            Assert.True(vm.Confirmations[0].CanAct);
        }

        [Fact]
        public void Window_TitleUsesAccountName()
        {
            SteamGuardAccount account = LoadPlainAccount();
            ConfirmationsWindowViewModel vm = CreateWindow(account, new RecordingClient());
            Assert.Equal("Trade Confirmations - fixture_user", vm.Title);
        }

        private static ConfirmationService CreateService(IConfirmationClient client, IAccessTokenRefresher refresher = null)
        {
            return new ConfirmationService(client, new SessionRefreshService(refresher ?? new RecordingRefresher()));
        }

        private static ConfirmationsWindowViewModel CreateWindow(SteamGuardAccount account, RecordingClient client)
        {
            return new ConfirmationsWindowViewModel(account, CreateService(client), PersistOk, new FailingIconLoader());
        }

        private static Task<string> PersistOk(SteamGuardAccount account, SessionData session)
        {
            return Task.FromResult<string>(null);
        }

        private static SteamGuardAccount AccountWithValidSession()
        {
            SteamGuardAccount account = LoadPlainAccount();
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = SyntheticJwt.Valid();
            return account;
        }

        private static Confirmation TradeConfirmation(ulong id = 11, string headline = "Trade with Fixture")
        {
            return new Confirmation
            {
                ID = id,
                Key = 22,
                Creator = 76561198000000002,
                Headline = headline,
                Summary = new List<string> { "You will give Fixture Item", "You will receive Fixture Item" },
                Accept = "Accept Trade",
                Cancel = "Cancel Trade",
                Icon = "",
                ConfType = Confirmation.EMobileConfirmationType.Trade
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

        private static async Task WaitUntil(Func<bool> condition)
        {
            for (int i = 0; i < 50; i++)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(20);
            }

            throw new TimeoutException();
        }

        private sealed class RecordingClient : IConfirmationClient
        {
            public int FetchCalls;
            public int AcceptCalls;
            public int DenyCalls;
            public Confirmation[] Next = new Confirmation[0];
            public Exception FetchError;
            public bool AcceptResult = true;
            public bool DenyResult = true;
            public Task<Confirmation[]> PendingFetch;
            public Task<bool> PendingAccept;

            public Task<Confirmation[]> FetchAsync(SteamGuardAccount account, CancellationToken cancellationToken)
            {
                FetchCalls++;
                cancellationToken.ThrowIfCancellationRequested();
                if (FetchError != null)
                {
                    throw FetchError;
                }

                if (PendingFetch != null)
                {
                    return PendingFetch;
                }

                return Task.FromResult(Next);
            }

            public Task<bool> AcceptAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken)
            {
                AcceptCalls++;
                cancellationToken.ThrowIfCancellationRequested();
                if (PendingAccept != null)
                {
                    return PendingAccept;
                }

                return Task.FromResult(AcceptResult);
            }

            public Task<bool> DenyAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken)
            {
                DenyCalls++;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(DenyResult);
            }

            public int AcceptMultipleCalls;
            public Confirmation[] LastBatch;

            public Task<bool> AcceptMultipleAsync(SteamGuardAccount account, Confirmation[] confirmations, CancellationToken cancellationToken)
            {
                AcceptMultipleCalls++;
                LastBatch = confirmations;
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(AcceptResult);
            }
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

        private sealed class FailingIconLoader : IConfirmationIconLoader
        {
            public Task<byte[]> LoadAsync(string url, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("icon unavailable");
            }
        }
    }
}
