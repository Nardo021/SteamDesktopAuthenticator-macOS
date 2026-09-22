using SDA.Core.Storage;
using SDA.Desktop.Services;
using SteamAuth;
using System;
using System.IO;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class EncryptionManagementTests : IDisposable
    {
        private readonly string _root;
        private readonly EncryptionManagementService _encryption = new EncryptionManagementService();

        public EncryptionManagementTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-enc-").FullName;
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
        public void Plaintext_EnablesEncryption()
        {
            string directory = NewDirectory();
            Phase6Fixtures.Destination(directory, false, null, Phase6Fixtures.ValidAccount());

            EncryptionChangeResult result = _encryption.Change(directory, null, "fixture-new-pass", "fixture-new-pass");

            Assert.Equal(EncryptionChangeStatus.Enabled, result.Status);
            Assert.Equal("fixture-new-pass", result.ActivePassKey);
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.Encrypted);
            Assert.False(File.ReadAllText(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile")).TrimStart().StartsWith("{"));
            Assert.Equal(AccountLoadStatus.Success, manifest.LoadAccounts("fixture-new-pass").Status);
        }

        [Fact]
        public void CorrectOldKey_ChangesEncryptionKey()
        {
            string directory = NewDirectory();
            Phase6Fixtures.Destination(directory, true, "fixture-old-pass", Phase6Fixtures.ValidAccount());

            EncryptionChangeResult result = _encryption.Change(directory, "fixture-old-pass", "fixture-new-pass", "fixture-new-pass");

            Assert.Equal(EncryptionChangeStatus.Changed, result.Status);
            Assert.Equal("fixture-new-pass", result.ActivePassKey);
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.Equal(AccountLoadStatus.InvalidPassword, manifest.LoadAccounts("fixture-old-pass").Status);
            Assert.Equal(AccountLoadStatus.Success, manifest.LoadAccounts("fixture-new-pass").Status);
        }

        [Fact]
        public void WrongOldKey_DoesNotMutate()
        {
            string directory = NewDirectory();
            Phase6Fixtures.Destination(directory, true, "fixture-old-pass", Phase6Fixtures.ValidAccount());
            string before = File.ReadAllText(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile"));

            EncryptionChangeResult result = _encryption.Change(directory, "wrong-pass", "fixture-new-pass", "fixture-new-pass");

            Assert.Equal(EncryptionChangeStatus.WrongCurrentKey, result.Status);
            Assert.Equal(before, File.ReadAllText(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile")));
            Assert.Equal(AccountLoadStatus.Success, Manifest.GetManifest(directory).LoadAccounts("fixture-old-pass").Status);
        }

        [Fact]
        public void BlankNewKey_RemovesEncryption()
        {
            string directory = NewDirectory();
            Phase6Fixtures.Destination(directory, true, "fixture-old-pass", Phase6Fixtures.ValidAccount());

            EncryptionChangeResult result = _encryption.Change(directory, "fixture-old-pass", "", "");

            Assert.Equal(EncryptionChangeStatus.Removed, result.Status);
            Assert.Null(result.ActivePassKey);
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.False(manifest.Encrypted);
            Assert.StartsWith("{", File.ReadAllText(Path.Combine(directory, Phase6Fixtures.SteamId + ".maFile")).TrimStart());
            Assert.Equal(Phase6Fixtures.SharedSecret, manifest.LoadAccounts(null).Accounts[0].SharedSecret);
        }

        [Fact]
        public void MismatchedNewKeys_DoNotMutate()
        {
            string directory = NewDirectory();
            Phase6Fixtures.Destination(directory, false, null, Phase6Fixtures.ValidAccount());
            string before = File.ReadAllText(Path.Combine(directory, "manifest.json"));

            EncryptionChangeResult result = _encryption.Change(directory, null, "one", "two");

            Assert.Equal(EncryptionChangeStatus.Mismatch, result.Status);
            Assert.Equal(before, File.ReadAllText(Path.Combine(directory, "manifest.json")));
            Assert.False(Manifest.GetManifest(directory).Encrypted);
        }

        [Fact]
        public void EmptyManifest_IsNoOp()
        {
            string directory = NewDirectory();
            Manifest.GenerateNewManifest(directory, false);

            Assert.Equal(EncryptionManagementKind.Empty, _encryption.Inspect(directory));
            EncryptionChangeResult result = _encryption.Change(directory, null, "fixture-pass", "fixture-pass");
            Assert.Equal(EncryptionChangeStatus.Empty, result.Status);
            Assert.False(Manifest.GetManifest(directory).Encrypted);
        }

        [Fact]
        public void AfterKeyChange_AllAccountsRemainReadableWithNewKeyOnly()
        {
            string directory = NewDirectory();
            SteamGuardAccount first = Phase6Fixtures.ValidAccount();
            SteamGuardAccount second = Phase6Fixtures.Account(SyntheticJwt.Valid(), SyntheticJwt.Valid(), 76561198000000088);
            second.AccountName = "fixture_second";
            Phase6Fixtures.Destination(directory, true, "fixture-old-pass", first, second);

            EncryptionChangeResult result = _encryption.Change(directory, "fixture-old-pass", "fixture-new-pass", "fixture-new-pass");

            Assert.True(result.Succeeded);
            AccountLoadResult loaded = Manifest.GetManifest(directory).LoadAccounts("fixture-new-pass");
            Assert.Equal(2, loaded.Accounts.Length);
            Assert.Equal(AccountLoadStatus.InvalidPassword, Manifest.GetManifest(directory).LoadAccounts("fixture-old-pass").Status);
            foreach (string path in Directory.GetFiles(directory, "*.maFile"))
            {
                Assert.False(File.ReadAllText(path).TrimStart().StartsWith("{"));
            }
        }

        [Fact]
        public void InMemoryActivePasskey_TracksEnableChangeAndRemoval()
        {
            string directory = NewDirectory();
            Phase6Fixtures.Destination(directory, false, null, Phase6Fixtures.ValidAccount());

            EncryptionChangeResult enabled = _encryption.Change(directory, null, "fixture-a", "fixture-a");
            Assert.Equal("fixture-a", enabled.ActivePassKey);

            EncryptionChangeResult changed = _encryption.Change(directory, "fixture-a", "fixture-b", "fixture-b");
            Assert.Equal("fixture-b", changed.ActivePassKey);

            EncryptionChangeResult removed = _encryption.Change(directory, "fixture-b", null, null);
            Assert.Null(removed.ActivePassKey);
        }

        private string NewDirectory()
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
