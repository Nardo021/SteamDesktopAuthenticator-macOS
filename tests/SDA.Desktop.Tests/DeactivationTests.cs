using SDA.Core.Storage;
using SDA.Desktop.Services;
using SteamAuth;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class DeactivationTests : IDisposable
    {
        private readonly string _root;

        public DeactivationTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-deact-").FullName;
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
        public async Task MissingSession_DoesNotCallRemote()
        {
            RecordingDeactivator remote = new RecordingDeactivator();
            AuthenticatorDeactivationService service = Create(remote);
            SteamGuardAccount account = Phase6Fixtures.MissingSessionAccount();

            DeactivationResult result = await service.PrepareSessionAsync(account, NewDirectory(), null, CancellationToken.None);

            Assert.Equal(DeactivationStatus.SessionExpired, result.Status);
            Assert.Equal(0, remote.Calls);
        }

        [Fact]
        public async Task ExpiredRefreshToken_DoesNotCallRemote()
        {
            RecordingDeactivator remote = new RecordingDeactivator();
            AuthenticatorDeactivationService service = Create(remote);
            SteamGuardAccount account = Phase6Fixtures.ExpiredRefreshAccount();

            DeactivationResult result = await service.PrepareSessionAsync(account, NewDirectory(), null, CancellationToken.None);

            Assert.Equal(DeactivationStatus.SessionExpired, result.Status);
            Assert.Equal(0, remote.Calls);
        }

        [Fact]
        public async Task ExpiredAccessToken_RefreshesBeforeRemote()
        {
            RecordingDeactivator remote = new RecordingDeactivator();
            RecordingRefresher refresher = new RecordingRefresher();
            AuthenticatorDeactivationService service = new AuthenticatorDeactivationService(remote, refresher, new SessionPersistenceService());
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.ExpiredAccessAccount();
            Phase6Fixtures.Destination(directory, false, null, account);

            DeactivationResult prepared = await service.PrepareSessionAsync(account, directory, null, CancellationToken.None);
            Assert.Equal(DeactivationStatus.Succeeded, prepared.Status);
            Assert.Equal(1, refresher.Calls);
            Assert.Equal(0, remote.Calls);

            DeactivationResult remoteResult = await service.DeactivateRemoteAsync(account, AuthenticatorDeactivationService.EmailScheme, CancellationToken.None);
            Assert.True(remoteResult.RemoteSucceeded);
            Assert.Equal(1, remote.Calls);
        }

        [Fact]
        public async Task Scheme1_IsPassedToDeactivator()
        {
            RecordingDeactivator remote = new RecordingDeactivator();
            AuthenticatorDeactivationService service = Create(remote);

            await service.DeactivateRemoteAsync(Phase6Fixtures.ValidAccount(), AuthenticatorDeactivationService.EmailScheme, CancellationToken.None);

            Assert.Equal(1, remote.Schemes[0]);
            Assert.Single(remote.Schemes);
        }

        [Fact]
        public async Task Scheme2_IsPassedToDeactivator()
        {
            RecordingDeactivator remote = new RecordingDeactivator();
            AuthenticatorDeactivationService service = Create(remote);

            await service.DeactivateRemoteAsync(Phase6Fixtures.ValidAccount(), AuthenticatorDeactivationService.RemoveCompletelyScheme, CancellationToken.None);

            Assert.Equal(2, remote.Schemes[0]);
            Assert.Single(remote.Schemes);
        }

        [Fact]
        public void ConfirmationMismatch_DoesNotCallRemote()
        {
            RecordingDeactivator remote = new RecordingDeactivator();
            AuthenticatorDeactivationService service = Create(remote);

            Assert.False(service.ConfirmationMatches("ABCDE", "XXXXX"));
            Assert.Equal(0, remote.Calls);
        }

        [Fact]
        public async Task RemoteFalse_LeavesLocalAccount()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.ValidAccount();
            Phase6Fixtures.Destination(directory, false, null, account);
            RecordingDeactivator remote = new RecordingDeactivator { Result = false };
            AuthenticatorDeactivationService service = Create(remote);

            DeactivationResult result = await service.DeactivateRemoteAsync(account, 2, CancellationToken.None);

            Assert.Equal(DeactivationStatus.RemoteFailed, result.Status);
            Assert.False(result.RemoteSucceeded);
            Assert.Single(Manifest.GetManifest(directory).Entries);
            Assert.True(File.Exists(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile")));
        }

        [Fact]
        public async Task RemoteSuccess_RemovesLocalOnlyAfterSuccess()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.ValidAccount();
            Phase6Fixtures.Destination(directory, false, null, account);
            RecordingDeactivator remote = new RecordingDeactivator();
            AuthenticatorDeactivationService service = Create(remote);
            string maFile = Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile");

            DeactivationResult remoteResult = await service.DeactivateRemoteAsync(account, 2, CancellationToken.None);

            Assert.True(remoteResult.RemoteSucceeded);
            Assert.True(File.Exists(maFile));
            Assert.Single(Manifest.GetManifest(directory).Entries);

            DeactivationResult local = service.CleanupLocalAfterRemoteSuccess(account, directory, 2);

            Assert.Equal(DeactivationStatus.Succeeded, local.Status);
            Assert.False(File.Exists(maFile));
            Assert.Empty(Manifest.GetManifest(directory).Entries);
        }

        [Fact]
        public async Task RemoteSuccess_LocalCleanupFailure_IsReported()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.ValidAccount();
            Phase6Fixtures.Destination(directory, false, null, account);
            RecordingDeactivator remote = new RecordingDeactivator();
            AuthenticatorDeactivationService service = Create(remote);
            string maFile = Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile");

            DeactivationResult remoteResult = await service.DeactivateRemoteAsync(account, 1, CancellationToken.None);
            Assert.True(remoteResult.RemoteSucceeded);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "not-a-manifest");

            DeactivationResult local = service.CleanupLocalAfterRemoteSuccess(account, directory, 1);

            Assert.Equal(DeactivationStatus.LocalCleanupFailed, local.Status);
            Assert.True(local.RemoteSucceeded);
            Assert.Equal(AuthenticatorDeactivationService.LocalCleanupFailedMessage, local.Message);
            Assert.True(File.Exists(maFile));
        }

        [Fact]
        public async Task DoubleSubmit_MakesOnlyOneRemoteCall()
        {
            BlockingDeactivator remote = new BlockingDeactivator();
            AuthenticatorDeactivationService service = new AuthenticatorDeactivationService(remote, new RecordingRefresher(), new SessionPersistenceService());
            SteamGuardAccount account = Phase6Fixtures.ValidAccount();

            Task<DeactivationResult> first = service.DeactivateRemoteAsync(account, 2, CancellationToken.None);
            await remote.Started.Task;
            DeactivationResult second = await service.DeactivateRemoteAsync(account, 2, CancellationToken.None);
            remote.Release.SetResult(true);
            DeactivationResult firstResult = await first;

            Assert.Equal(DeactivationStatus.Busy, second.Status);
            Assert.True(firstResult.RemoteSucceeded);
            Assert.Equal(1, remote.Calls);
        }

        private static AuthenticatorDeactivationService Create(IAuthenticatorDeactivator remote)
        {
            return new AuthenticatorDeactivationService(remote, new RecordingRefresher(), new SessionPersistenceService());
        }

        private string NewDirectory()
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private sealed class RecordingDeactivator : IAuthenticatorDeactivator
        {
            public bool Result = true;
            public int Calls;
            public readonly System.Collections.Generic.List<int> Schemes = new System.Collections.Generic.List<int>();

            public Task<bool> DeactivateAsync(SteamGuardAccount account, int scheme, CancellationToken cancellationToken)
            {
                Calls++;
                Schemes.Add(scheme);
                return Task.FromResult(Result);
            }
        }

        private sealed class RecordingRefresher : IAccessTokenRefresher
        {
            public int Calls;
            public string NextAccess = SyntheticJwt.Valid();

            public Task RefreshAsync(SessionData session, CancellationToken cancellationToken)
            {
                Calls++;
                session.AccessToken = NextAccess;
                return Task.CompletedTask;
            }
        }

        private sealed class BlockingDeactivator : IAuthenticatorDeactivator
        {
            public int Calls;
            public readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<bool> Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<bool> DeactivateAsync(SteamGuardAccount account, int scheme, CancellationToken cancellationToken)
            {
                Calls++;
                Started.TrySetResult(true);
                return await Release.Task;
            }
        }
    }
}
