using Newtonsoft.Json;
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
    public class PeriodicConfirmationTests : IDisposable
    {
        private readonly string _root;

        public PeriodicConfirmationTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-periodic-").FullName;
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
        public async Task Disabled_DoesNotPoll()
        {
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { Trade(1) } };
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("a")));
            service.ApplySettings(new ManifestRuntimeSettings { PeriodicChecking = false, PeriodicCheckingInterval = 5 });

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(0, result.Fetches);
            Assert.Equal(0, client.FetchCalls);
        }

        [Fact]
        public async Task Enabled_PollsSelectedAccount()
        {
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { Trade(1) } };
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("selected")));
            service.ApplySettings(Enabled());

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(1, result.Fetches);
            Assert.Equal(1, client.FetchCalls);
            Assert.Equal(1, result.PopupQueued);
        }

        [Fact]
        public void MinimumInterval_ClampsRuntimeValue()
        {
            PeriodicConfirmationService service = CreateService(new RecordingClient(), Snapshot());
            service.ApplySettings(new ManifestRuntimeSettings { PeriodicChecking = true, PeriodicCheckingInterval = 2 });

            Assert.Equal(5, service.EffectiveIntervalSeconds);
        }

        [Fact]
        public async Task OverlappingTick_IsSkipped()
        {
            TaskCompletionSource<Confirmation[]> pending = new TaskCompletionSource<Confirmation[]>();
            RecordingClient client = new RecordingClient { PendingFetch = pending.Task };
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("a")));
            service.ApplySettings(Enabled());

            Task<PeriodicPollResult> first = service.PollAsync();
            for (int i = 0; i < 50 && client.FetchCalls == 0; i++)
            {
                await Task.Delay(10);
            }

            PeriodicPollResult second = await service.PollAsync();
            pending.SetResult(new Confirmation[0]);
            PeriodicPollResult completed = await first;

            Assert.True(second.OverlapSkipped);
            Assert.Equal(1, service.OverlappedTicksSkipped);
            Assert.Equal(1, completed.Fetches);
        }

        [Fact]
        public async Task CheckAllFalse_UsesSnapshotOnly()
        {
            RecordingClient client = new RecordingClient { Next = new Confirmation[0] };
            SteamGuardAccount selected = ValidAccount("one");
            PeriodicConfirmationService service = CreateService(client, Snapshot(selected));
            service.ApplySettings(Enabled(checkAll: false));

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(1, result.AccountsInspected);
            Assert.Equal(1, client.FetchCalls);
        }

        [Fact]
        public async Task CheckAllTrue_PollsEverySnapshotAccount()
        {
            RecordingClient client = new RecordingClient { Next = new Confirmation[0] };
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("one"), ValidAccount("two")));
            service.ApplySettings(Enabled(checkAll: true));

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(2, result.AccountsInspected);
            Assert.Equal(2, client.FetchCalls);
        }

        [Fact]
        public async Task NoSelectedAccountAndCheckAllFalse_DoesNoWork()
        {
            RecordingClient client = new RecordingClient();
            PeriodicConfirmationService service = CreateService(client, Snapshot());
            service.ApplySettings(Enabled(checkAll: false));

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(0, result.AccountsInspected);
            Assert.Equal(0, client.FetchCalls);
        }

        [Fact]
        public async Task ExpiredRefreshToken_DoesNotFetch()
        {
            SteamGuardAccount account = ValidAccount("expired");
            account.Session.RefreshToken = SyntheticJwt.Expired();
            RecordingClient client = new RecordingClient();
            List<string> status = new List<string>();
            PeriodicConfirmationService service = CreateService(client, Snapshot(account));
            service.StatusRaised += status.Add;
            service.ApplySettings(Enabled());

            PeriodicPollResult first = await service.PollAsync();
            PeriodicPollResult second = await service.PollAsync();

            Assert.Equal(1, first.SkippedExpiredRefresh);
            Assert.Equal(0, client.FetchCalls);
            Assert.Single(status);
            Assert.Contains("expired", status[0]);
            Assert.Equal(1, second.SkippedExpiredRefresh);
        }

        [Fact]
        public async Task ExpiredAccessToken_RefreshesPersistsAndFetches()
        {
            SteamGuardAccount account = ValidAccount("refresh");
            account.Session.AccessToken = SyntheticJwt.Expired();
            RecordingClient client = new RecordingClient { Next = new Confirmation[0] };
            RecordingRefresher refresher = new RecordingRefresher();
            int persistCalls = 0;
            PeriodicConfirmationService service = CreateService(
                client,
                Snapshot(account),
                refresher,
                (target, session) =>
                {
                    persistCalls++;
                    return Task.FromResult<string>(null);
                });
            service.ApplySettings(Enabled());

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(1, refresher.Calls);
            Assert.Equal(1, persistCalls);
            Assert.Equal(1, result.Fetches);
            Assert.Equal(1, client.FetchCalls);
        }

        [Fact]
        public async Task MarketAutoConfirm_AcceptsBatch()
        {
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { Market(7), Market(8) } };
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("market")));
            service.ApplySettings(Enabled(market: true));

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(2, result.AutoAccepted);
            Assert.Equal(1, client.AcceptMultipleCalls);
            Assert.Equal(2, client.LastBatch.Length);
            Assert.Equal(0, result.PopupQueued);
        }

        [Fact]
        public async Task TradeAutoConfirm_Accepts()
        {
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { Trade(3) } };
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("trade")));
            service.ApplySettings(Enabled(trades: true));

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(1, result.AutoAccepted);
            Assert.Equal(1, client.AcceptMultipleCalls);
            Assert.Equal(0, result.PopupQueued);
        }

        [Fact]
        public async Task MarketWithoutAutoConfirm_GoesToPopup()
        {
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { Market(4) } };
            ConfirmationPopupViewModel popup = new ConfirmationPopupViewModel(client);
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("market")), popup: popup);
            service.ApplySettings(Enabled(market: false));

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(1, result.PopupQueued);
            Assert.Equal(0, client.AcceptMultipleCalls);
            Assert.Equal(Confirmation.EMobileConfirmationType.MarketListing, popup.CurrentConfirmation.ConfType);
        }

        [Fact]
        public async Task TradeWithoutAutoConfirm_GoesToPopup()
        {
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { Trade(5) } };
            ConfirmationPopupViewModel popup = new ConfirmationPopupViewModel(client);
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("trade")), popup: popup);
            service.ApplySettings(Enabled(trades: false));

            await service.PollAsync();

            Assert.Equal(Confirmation.EMobileConfirmationType.Trade, popup.CurrentConfirmation.ConfType);
        }

        [Fact]
        public async Task OtherConfirmationType_GoesToPopup()
        {
            Confirmation other = new Confirmation
            {
                ID = 9,
                Key = 10,
                ConfType = Confirmation.EMobileConfirmationType.PhoneNumberChange
            };
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { other } };
            ConfirmationPopupViewModel popup = new ConfirmationPopupViewModel(client);
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("other")), popup: popup);
            service.ApplySettings(Enabled(market: true, trades: true));

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(1, result.PopupQueued);
            Assert.Equal(0, client.AcceptMultipleCalls);
        }

        [Fact]
        public async Task FailedAutoConfirm_IsNotMarkedResolved()
        {
            RecordingClient client = new RecordingClient
            {
                Next = new Confirmation[] { Trade(6) },
                AcceptResult = false
            };
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("fail")));
            service.ApplySettings(Enabled(trades: true));

            PeriodicPollResult result = await service.PollAsync();

            Assert.Equal(0, result.AutoAccepted);
            Assert.Equal(1, client.AcceptMultipleCalls);
        }

        [Fact]
        public async Task DuplicateConfirmation_IsNotQueuedTwice()
        {
            Confirmation trade = Trade(11);
            RecordingClient client = new RecordingClient { Next = new Confirmation[] { trade } };
            ConfirmationPopupViewModel popup = new ConfirmationPopupViewModel(client);
            PeriodicConfirmationService service = CreateService(client, Snapshot(ValidAccount("dup")), popup: popup);
            service.ApplySettings(Enabled());

            PeriodicPollResult first = await service.PollAsync();
            PeriodicPollResult second = await service.PollAsync();

            Assert.Equal(1, first.PopupQueued);
            Assert.Equal(0, second.PopupQueued);
            Assert.Equal(1, popup.QueueCount);
        }

        private PeriodicConfirmationService CreateService(
            RecordingClient client,
            Func<PeriodicPollSnapshot> snapshot,
            RecordingRefresher refresher = null,
            Func<SteamGuardAccount, SessionData, Task<string>> persist = null,
            ConfirmationPopupViewModel popup = null)
        {
            ConfirmationService confirmations = new ConfirmationService(
                client,
                new SessionRefreshService(refresher ?? new RecordingRefresher()));
            return new PeriodicConfirmationService(
                confirmations,
                client,
                snapshot,
                persist ?? ((account, session) => Task.FromResult<string>(null)),
                popup ?? new ConfirmationPopupViewModel(client));
        }

        private static ManifestRuntimeSettings Enabled(bool checkAll = false, bool market = false, bool trades = false)
        {
            return new ManifestRuntimeSettings
            {
                PeriodicChecking = true,
                PeriodicCheckingInterval = 5,
                CheckAllAccounts = checkAll,
                AutoConfirmMarketTransactions = market,
                AutoConfirmTrades = trades
            };
        }

        private static Func<PeriodicPollSnapshot> Snapshot(params SteamGuardAccount[] accounts)
        {
            return () => new PeriodicPollSnapshot(accounts, null, null);
        }

        private SteamGuardAccount ValidAccount(string name)
        {
            SteamGuardAccount account = LoadPlainAccount();
            account.AccountName = name;
            account.Session.RefreshToken = SyntheticJwt.Valid();
            account.Session.AccessToken = SyntheticJwt.Valid();
            return account;
        }

        private static Confirmation Trade(ulong id)
        {
            return new Confirmation { ID = id, Key = id + 100, ConfType = Confirmation.EMobileConfirmationType.Trade };
        }

        private static Confirmation Market(ulong id)
        {
            return new Confirmation { ID = id, Key = id + 200, ConfType = Confirmation.EMobileConfirmationType.MarketListing };
        }

        private static SteamGuardAccount LoadPlainAccount()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fixture-plain-mafile.json");
            return JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(fixture));
        }

        private sealed class RecordingClient : IConfirmationClient
        {
            public int FetchCalls;
            public int AcceptCalls;
            public int DenyCalls;
            public int AcceptMultipleCalls;
            public Confirmation[] Next = new Confirmation[0];
            public Confirmation[] LastBatch;
            public bool AcceptResult = true;
            public bool DenyResult = true;
            public Task<Confirmation[]> PendingFetch;

            public Task<Confirmation[]> FetchAsync(SteamGuardAccount account, CancellationToken cancellationToken)
            {
                FetchCalls++;
                if (PendingFetch != null)
                {
                    return PendingFetch;
                }

                return Task.FromResult(Next);
            }

            public Task<bool> AcceptAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken)
            {
                AcceptCalls++;
                return Task.FromResult(AcceptResult);
            }

            public Task<bool> DenyAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken)
            {
                DenyCalls++;
                return Task.FromResult(DenyResult);
            }

            public Task<bool> AcceptMultipleAsync(SteamGuardAccount account, Confirmation[] confirmations, CancellationToken cancellationToken)
            {
                AcceptMultipleCalls++;
                LastBatch = confirmations;
                return Task.FromResult(AcceptResult);
            }
        }

        private sealed class RecordingRefresher : IAccessTokenRefresher
        {
            public int Calls;

            public Task RefreshAsync(SessionData session, CancellationToken cancellationToken)
            {
                Calls++;
                session.AccessToken = SyntheticJwt.Valid();
                return Task.CompletedTask;
            }
        }
    }
}
