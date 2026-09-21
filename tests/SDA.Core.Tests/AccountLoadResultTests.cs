using Newtonsoft.Json;
using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;
using Xunit;

namespace SDA.Core.Tests
{
    public class AccountLoadResultTests : IDisposable
    {
        private readonly string _root;

        public AccountLoadResultTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-load-").FullName;
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
        public void UnencryptedManifest_LoadsWithoutPassword()
        {
            string directory = CreatePlainDirectory();

            AccountLoadResult result = Manifest.GetManifest(directory).LoadAccounts(null);

            Assert.Equal(AccountLoadStatus.Success, result.Status);
            Assert.Single(result.Accounts);
            Assert.Equal("fixture_user", result.Accounts[0].AccountName);
        }

        [Fact]
        public void EmptyManifest_IsNoAccountsRatherThanPasswordFailure()
        {
            string directory = NewDirectory();
            Manifest.GetManifest(directory);

            AccountLoadResult result = Manifest.GetManifest(directory).LoadAccounts(null);

            Assert.Equal(AccountLoadStatus.NoAccounts, result.Status);
            Assert.Empty(result.Accounts);
        }

        [Fact]
        public void EncryptedManifest_RequiresPasswordBeforeReturningAccounts()
        {
            string directory = CreateEncryptedDirectory("fixture-pass");
            Manifest manifest = Manifest.GetManifest(directory);

            AccountLoadResult missing = manifest.LoadAccounts(null);
            AccountLoadResult wrong = manifest.LoadAccounts("wrong-passkey");
            AccountLoadResult right = manifest.LoadAccounts("fixture-pass");

            Assert.Equal(AccountLoadStatus.PasswordRequired, missing.Status);
            Assert.Empty(missing.Accounts);
            Assert.Equal(AccountLoadStatus.InvalidPassword, wrong.Status);
            Assert.Empty(wrong.Accounts);
            Assert.Equal(AccountLoadStatus.Success, right.Status);
            Assert.Equal("fixture_user", right.Accounts[0].AccountName);
            Assert.Empty(manifest.GetAllAccounts("wrong-passkey"));
        }

        [Fact]
        public void WrongPassword_DoesNotRewriteTheMaFile()
        {
            string directory = CreateEncryptedDirectory("fixture-pass");
            string maFile = Path.Combine(directory, "76561198000000001.maFile");
            string before = File.ReadAllText(maFile);

            Manifest.GetManifest(directory).LoadAccounts("wrong-passkey");

            Assert.Equal(before, File.ReadAllText(maFile));
        }

        private string CreatePlainDirectory()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.SaveAccount(LoadPlainAccount(), false));
            return directory;
        }

        private string CreateEncryptedDirectory(string passKey)
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.SaveAccount(LoadPlainAccount(), true, passKey));
            return directory;
        }

        private static SteamGuardAccount LoadPlainAccount()
        {
            return JsonConvert.DeserializeObject<SteamGuardAccount>(FixtureFiles.Read("fixture-plain-mafile.json"));
        }

        private string NewDirectory()
        {
            return Path.Combine(_root, Guid.NewGuid().ToString("N"));
        }
    }
}
