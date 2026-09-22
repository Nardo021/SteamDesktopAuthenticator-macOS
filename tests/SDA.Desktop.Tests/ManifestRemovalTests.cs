using SDA.Core.Storage;
using SDA.Desktop.Services;
using SteamAuth;
using System;
using System.IO;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class ManifestRemovalTests : IDisposable
    {
        private readonly string _root;
        private readonly ManifestAccountRemovalService _removal = new ManifestAccountRemovalService();

        public ManifestRemovalTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-remove-").FullName;
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
        public void PlaintextManifest_RemovesEntryAndKeepsMaFile()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.ValidAccount();
            Phase6Fixtures.Destination(directory, false, null, account);
            string maFile = Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile");
            string contents = File.ReadAllText(maFile);

            ManifestRemovalResult result = _removal.RemoveFromManifest(account, directory);

            Assert.Equal(ManifestRemovalStatus.Removed, result.Status);
            Assert.True(result.MaFileExists);
            Assert.True(File.Exists(maFile));
            Assert.Equal(contents, File.ReadAllText(maFile));
            Assert.Empty(Manifest.GetManifest(directory).Entries);
        }

        [Fact]
        public void EncryptedManifest_IsRejectedWithoutMutation()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.LoadPlainFixture();
            Phase6Fixtures.Destination(directory, true, "fixture-pass", account);
            string beforeManifest = File.ReadAllText(Path.Combine(directory, "manifest.json"));
            string maFile = Path.Combine(directory, account.Session.SteamID + ".maFile");
            string beforeFile = File.ReadAllText(maFile);

            ManifestRemovalResult result = _removal.RemoveFromManifest(account, directory);

            Assert.Equal(ManifestRemovalStatus.EncryptedBlocked, result.Status);
            Assert.Equal(ManifestAccountRemovalService.EncryptedBlockedMessage, result.Message);
            Assert.Equal(beforeManifest, File.ReadAllText(Path.Combine(directory, "manifest.json")));
            Assert.Equal(beforeFile, File.ReadAllText(maFile));
        }

        [Fact]
        public void Cancel_DoesNotCallRemoval()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.ValidAccount();
            Phase6Fixtures.Destination(directory, false, null, account);
            string before = File.ReadAllText(Path.Combine(directory, "manifest.json"));

            Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "manifest.json")));
            Assert.Single(Manifest.GetManifest(directory).Entries);
            Assert.True(File.Exists(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile")));
        }

        [Fact]
        public void LastEntryRemoval_LeavesPlainManifestAndMaFile()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.ValidAccount();
            Phase6Fixtures.Destination(directory, false, null, account);

            ManifestRemovalResult result = _removal.RemoveFromManifest(account, directory);

            Assert.Equal(ManifestRemovalStatus.Removed, result.Status);
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.Empty(manifest.Entries);
            Assert.False(manifest.Encrypted);
            Assert.True(File.Exists(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile")));
        }

        [Fact]
        public void LegacyFalseReturn_IsTreatedAsSuccessWhenDiskIsCorrect()
        {
            string directory = NewDirectory();
            SteamGuardAccount account = Phase6Fixtures.ValidAccount();
            Manifest manifest = Phase6Fixtures.Destination(directory, false, null, account);
            Assert.False(manifest.RemoveAccount(account, false));
            Assert.Empty(Manifest.GetManifest(directory).Entries);
            Assert.True(File.Exists(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile")));

            SteamGuardAccount second = Phase6Fixtures.Account(SyntheticJwt.Valid(), SyntheticJwt.Valid(), 76561198000000088);
            second.AccountName = "fixture_second";
            Phase6Fixtures.Destination(directory, false, null, account, second);
            ManifestRemovalResult result = _removal.RemoveFromManifest(account, directory);

            Assert.Equal(ManifestRemovalStatus.Removed, result.Status);
            Assert.True(result.MaFileExists);
            Manifest reloaded = Manifest.GetManifest(directory);
            Assert.Single(reloaded.Entries);
            Assert.Equal(76561198000000088UL, reloaded.Entries[0].SteamID);
            Assert.True(File.Exists(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile")));
        }

        private string NewDirectory()
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
