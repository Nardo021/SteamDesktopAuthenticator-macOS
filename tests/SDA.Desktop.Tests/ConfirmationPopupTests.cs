using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class ConfirmationPopupTests
    {
        [Fact]
        public void Enqueue_DisplaysOneConfirmation()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = new ConfirmationPopupViewModel(client);
            SteamGuardAccount account = Owner("alpha");
            Confirmation confirmation = Trade(1);

            Assert.True(vm.Enqueue(account, confirmation));

            Assert.True(vm.IsVisible);
            Assert.Equal("alpha", vm.AccountName);
            Assert.Same(account, vm.CurrentAccount);
            Assert.Equal(1UL, vm.CurrentConfirmation.ID);
            Assert.Equal(1, vm.QueueCount);
        }

        [Fact]
        public async Task FirstAccept_DoesNotCallRemote()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = Loaded(client);

            await vm.AcceptAsync();

            Assert.Equal(0, client.AcceptCalls);
            Assert.True(vm.AcceptArmed);
            Assert.Equal(ConfirmationPopupViewModel.AcceptAgainText, vm.StatusText);
            Assert.True(vm.IsVisible);
        }

        [Fact]
        public async Task SecondAccept_CallsRemoteOnce()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = Loaded(client);

            await vm.AcceptAsync();
            await vm.AcceptAsync();

            Assert.Equal(1, client.AcceptCalls);
            Assert.False(vm.IsVisible);
        }

        [Fact]
        public async Task FirstDeny_DoesNotCallRemote()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = Loaded(client);

            await vm.DenyAsync();

            Assert.Equal(0, client.DenyCalls);
            Assert.True(vm.DenyArmed);
            Assert.Equal(ConfirmationPopupViewModel.DenyAgainText, vm.StatusText);
        }

        [Fact]
        public async Task SecondDeny_CallsRemoteOnce()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = Loaded(client);

            await vm.DenyAsync();
            await vm.DenyAsync();

            Assert.Equal(1, client.DenyCalls);
            Assert.False(vm.IsVisible);
        }

        [Fact]
        public async Task SwitchAcceptToDeny_RequiresSecondDeny()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = Loaded(client);

            await vm.AcceptAsync();
            await vm.DenyAsync();

            Assert.Equal(0, client.AcceptCalls);
            Assert.Equal(0, client.DenyCalls);
            Assert.True(vm.DenyArmed);
            Assert.False(vm.AcceptArmed);

            await vm.DenyAsync();

            Assert.Equal(1, client.DenyCalls);
        }

        [Fact]
        public async Task FailedAccept_KeepsItemAndAllowsRetry()
        {
            RecordingClient client = new RecordingClient { AcceptResult = false };
            ConfirmationPopupViewModel vm = Loaded(client);

            await vm.AcceptAsync();
            await vm.AcceptAsync();

            Assert.Equal(1, client.AcceptCalls);
            Assert.True(vm.IsVisible);
            Assert.False(vm.AcceptArmed);
            Assert.Equal(ConfirmationPopupViewModel.AcceptFailedText, vm.StatusText);

            client.AcceptResult = true;
            await vm.AcceptAsync();
            await vm.AcceptAsync();

            Assert.Equal(2, client.AcceptCalls);
            Assert.False(vm.IsVisible);
        }

        [Fact]
        public async Task SuccessfulAccept_AdvancesToNextItem()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = new ConfirmationPopupViewModel(client);
            SteamGuardAccount firstOwner = Owner("first");
            SteamGuardAccount secondOwner = Owner("second");
            vm.Enqueue(firstOwner, Trade(1));
            vm.Enqueue(secondOwner, Trade(2));

            await vm.AcceptAsync();
            await vm.AcceptAsync();

            Assert.True(vm.IsVisible);
            Assert.Equal("second", vm.AccountName);
            Assert.Same(secondOwner, vm.CurrentAccount);
            Assert.Equal(2UL, vm.CurrentConfirmation.ID);
        }

        [Fact]
        public async Task SuccessfulLastItem_HidesPopup()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = Loaded(client);

            await vm.AcceptAsync();
            await vm.AcceptAsync();

            Assert.False(vm.IsVisible);
            Assert.Equal(0, vm.QueueCount);
        }

        [Fact]
        public async Task ConfirmationRetainsOwningAccount()
        {
            RecordingClient client = new RecordingClient();
            ConfirmationPopupViewModel vm = new ConfirmationPopupViewModel(client);
            SteamGuardAccount owner = Owner("owner");
            vm.Enqueue(owner, Trade(4));

            await vm.AcceptAsync();
            await vm.AcceptAsync();

            Assert.Same(owner, client.LastAccount);
        }

        private static ConfirmationPopupViewModel Loaded(RecordingClient client)
        {
            ConfirmationPopupViewModel vm = new ConfirmationPopupViewModel(client);
            vm.Enqueue(Owner("alpha"), Trade(1));
            return vm;
        }

        private static SteamGuardAccount Owner(string name)
        {
            return new SteamGuardAccount
            {
                AccountName = name,
                Session = new SessionData { SteamID = (ulong)name.GetHashCode() }
            };
        }

        private static Confirmation Trade(ulong id)
        {
            return new Confirmation { ID = id, Key = id + 50, ConfType = Confirmation.EMobileConfirmationType.Trade };
        }

        private sealed class RecordingClient : IConfirmationClient
        {
            public int AcceptCalls;
            public int DenyCalls;
            public bool AcceptResult = true;
            public bool DenyResult = true;
            public SteamGuardAccount LastAccount;

            public Task<Confirmation[]> FetchAsync(SteamGuardAccount account, CancellationToken cancellationToken)
            {
                return Task.FromResult(new Confirmation[0]);
            }

            public Task<bool> AcceptAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken)
            {
                AcceptCalls++;
                LastAccount = account;
                return Task.FromResult(AcceptResult);
            }

            public Task<bool> DenyAsync(SteamGuardAccount account, Confirmation confirmation, CancellationToken cancellationToken)
            {
                DenyCalls++;
                LastAccount = account;
                return Task.FromResult(DenyResult);
            }

            public Task<bool> AcceptMultipleAsync(SteamGuardAccount account, Confirmation[] confirmations, CancellationToken cancellationToken)
            {
                return Task.FromResult(AcceptResult);
            }
        }
    }
}
