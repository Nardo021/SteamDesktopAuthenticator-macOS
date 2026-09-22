using Newtonsoft.Json;
using SDA.Core.Storage;
using SDA.Desktop.Services;
using SteamAuth;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SDA.Desktop.Tests
{
    public class AccountImportTests : IDisposable
    {
        private readonly string _root;
        private readonly AccountImportService _importer = new AccountImportService();

        public AccountImportTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-import-").FullName;
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
        public async Task PlainSource_ImportsIntoDestination()
        {
            string dest = NewDirectory();
            string source = WriteValidSource();

            AccountImportResult result = await _importer.ImportAsync(source, null, dest, NoLogin, null);

            Assert.True(result.Succeeded);
            Manifest manifest = Manifest.GetManifest(dest);
            Assert.Single(manifest.Entries);
            SteamGuardAccount loaded = manifest.LoadAccounts(null).Accounts[0];
            Assert.Equal(Phase6Fixtures.AccountName, loaded.AccountName);
            Assert.Equal(Phase6Fixtures.SharedSecret, loaded.SharedSecret);
            Assert.StartsWith("{", File.ReadAllText(Path.Combine(dest, Phase6Fixtures.SteamId + ".maFile")).TrimStart());
            Assert.True(File.Exists(source));
        }

        [Fact]
        public async Task InvalidJson_DoesNotMutateDestination()
        {
            string dest = NewDirectory();
            Phase6Fixtures.Destination(dest, false, null, Phase6Fixtures.LoadPlainFixture());
            string before = File.ReadAllText(Path.Combine(dest, "manifest.json"));
            string source = Path.Combine(NewDirectory(), "broken.maFile");
            File.WriteAllText(source, "not-json");

            AccountImportResult result = await _importer.ImportAsync(source, null, dest, NoLogin, null);

            Assert.Equal(AccountImportStatus.InvalidMaFile, result.Status);
            Assert.Equal(before, File.ReadAllText(Path.Combine(dest, "manifest.json")));
        }

        [Fact]
        public async Task EncryptedSource_CorrectKey_ImportsDecrypted()
        {
            string dest = NewDirectory();
            string sourceDir = NewDirectory();
            string source = Phase6Fixtures.WriteEncryptedMaFile(sourceDir, Phase6Fixtures.ValidAccount(), "fixture-source-pass", "76561198000000077.maFile");

            AccountImportResult result = await _importer.ImportAsync(source, "fixture-source-pass", dest, NoLogin, null);

            Assert.True(result.Succeeded);
            string destFile = File.ReadAllText(Path.Combine(dest, Phase6Fixtures.SteamId + ".maFile"));
            Assert.StartsWith("{", destFile.TrimStart());
            Assert.DoesNotContain("fixture-source-pass", destFile);
            Assert.False(File.ReadAllText(source).TrimStart().StartsWith("{"));
        }

        [Fact]
        public async Task EncryptedSource_WrongKey_DoesNotMutateDestination()
        {
            string dest = NewDirectory();
            string source = Phase6Fixtures.WriteEncryptedMaFile(NewDirectory(), Phase6Fixtures.ValidAccount(), "fixture-source-pass", "76561198000000077.maFile");

            AccountImportResult result = await _importer.ImportAsync(source, "wrong-passkey", dest, NoLogin, null);

            Assert.Equal(AccountImportStatus.DecryptionFailed, result.Status);
            Assert.False(File.Exists(Path.Combine(dest, "manifest.json")));
        }

        [Fact]
        public async Task EncryptedSource_MissingAdjacentManifest_Fails()
        {
            string dest = NewDirectory();
            string sourceDir = NewDirectory();
            string source = Path.Combine(sourceDir, "76561198000000077.maFile");
            File.WriteAllText(source, "ZW5jcnlwdGVk");

            AccountImportResult result = await _importer.ImportAsync(source, "fixture-source-pass", dest, NoLogin, null);

            Assert.Equal(AccountImportStatus.MissingAdjacentManifest, result.Status);
            Assert.False(File.Exists(Path.Combine(dest, "manifest.json")));
        }

        [Fact]
        public async Task EncryptedSource_EntryMissing_Fails()
        {
            string dest = NewDirectory();
            string sourceDir = NewDirectory();
            Phase6Fixtures.WriteEncryptedMaFile(sourceDir, Phase6Fixtures.ValidAccount(), "fixture-source-pass", "other.maFile");
            string source = Path.Combine(sourceDir, "76561198000000077.maFile");
            File.WriteAllText(source, File.ReadAllText(Path.Combine(sourceDir, "other.maFile")));

            AccountImportResult result = await _importer.ImportAsync(source, "fixture-source-pass", dest, NoLogin, null);

            Assert.Equal(AccountImportStatus.EntryNotFound, result.Status);
        }

        [Fact]
        public async Task EncryptedSource_MissingSaltAndIv_Fails()
        {
            string dest = NewDirectory();
            string sourceDir = NewDirectory();
            string source = Phase6Fixtures.WriteEncryptedMaFile(sourceDir, Phase6Fixtures.ValidAccount(), "fixture-source-pass", "76561198000000077.maFile");
            File.WriteAllText(Path.Combine(sourceDir, "manifest.json"), "{\"encrypted\":true,\"entries\":[{\"filename\":\"76561198000000077.maFile\",\"steamid\":76561198000000077}]}");

            AccountImportResult result = await _importer.ImportAsync(source, "fixture-source-pass", dest, NoLogin, null);

            Assert.Equal(AccountImportStatus.MissingSaltAndIv, result.Status);
        }

        [Fact]
        public async Task DestinationEncrypted_IsRejected()
        {
            string dest = NewDirectory();
            Phase6Fixtures.Destination(dest, true, "fixture-dest-pass", Phase6Fixtures.LoadPlainFixture());
            string before = File.ReadAllText(Path.Combine(dest, "manifest.json"));
            string source = WriteValidSource();

            AccountImportResult result = await _importer.ImportAsync(source, null, dest, NoLogin, null);

            Assert.Equal(AccountImportStatus.DestinationEncrypted, result.Status);
            Assert.Equal(AccountImportService.DestinationEncryptedMessage, result.Message);
            Assert.Equal(before, File.ReadAllText(Path.Combine(dest, "manifest.json")));
            Assert.False(File.Exists(Path.Combine(dest, Phase6Fixtures.SteamId + ".maFile")));
        }

        [Fact]
        public void MissingSession_RequiresImportLogin()
        {
            Assert.True(_importer.NeedsImportLogin(Phase6Fixtures.MissingSessionAccount()));
        }

        [Fact]
        public void ExpiredSession_RequiresImportLogin()
        {
            Assert.True(_importer.NeedsImportLogin(Phase6Fixtures.ExpiredAccessAccount()));
        }

        [Fact]
        public async Task ImportLoginCancelled_DoesNotMutateDestination()
        {
            string dest = NewDirectory();
            SteamGuardAccount sourceAccount = Phase6Fixtures.MissingSessionAccount();
            string source = Phase6Fixtures.WriteMaFile(NewDirectory(), sourceAccount);

            AccountImportResult result = await _importer.ImportAsync(source, null, dest, _ => Task.FromResult<SessionData>(null), null);

            Assert.Equal(AccountImportStatus.LoginCancelled, result.Status);
            Assert.False(File.Exists(Path.Combine(dest, "manifest.json")));
        }

        [Fact]
        public async Task ImportLoginSuccess_SavesReturnedSession()
        {
            string dest = NewDirectory();
            SteamGuardAccount sourceAccount = Phase6Fixtures.MissingSessionAccount();
            string source = Phase6Fixtures.WriteMaFile(NewDirectory(), sourceAccount);
            SessionData session = new SessionData
            {
                SteamID = Phase6Fixtures.SteamId,
                AccessToken = SyntheticJwt.Valid(),
                RefreshToken = SyntheticJwt.Valid()
            };

            AccountImportResult result = await _importer.ImportAsync(source, null, dest, _ => Task.FromResult(session), null);

            Assert.True(result.Succeeded);
            SteamGuardAccount loaded = Manifest.GetManifest(dest).LoadAccounts(null).Accounts[0];
            Assert.Equal(session.AccessToken, loaded.Session.AccessToken);
            Assert.Equal(session.RefreshToken, loaded.Session.RefreshToken);
            Assert.Equal(Phase6Fixtures.SteamId, loaded.Session.SteamID);
        }

        [Fact]
        public async Task SameSteamId_UsesSaveAccountReplacement()
        {
            string dest = NewDirectory();
            SteamGuardAccount existing = Phase6Fixtures.ValidAccount();
            existing.AccountName = "fixture_old";
            Phase6Fixtures.Destination(dest, false, null, existing);
            SteamGuardAccount incoming = Phase6Fixtures.ValidAccount();
            incoming.AccountName = "fixture_new";
            string source = Phase6Fixtures.WriteMaFile(NewDirectory(), incoming);

            AccountImportResult result = await _importer.ImportAsync(source, null, dest, NoLogin, _ => Task.FromResult(true));

            Assert.Equal(AccountImportStatus.Replaced, result.Status);
            Manifest manifest = Manifest.GetManifest(dest);
            Assert.Single(manifest.Entries);
            Assert.Equal("fixture_new", manifest.LoadAccounts(null).Accounts[0].AccountName);
        }

        private string WriteValidSource()
        {
            return Phase6Fixtures.WriteMaFile(NewDirectory(), Phase6Fixtures.ValidAccount());
        }

        private string NewDirectory()
        {
            string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static Task<SessionData> NoLogin(SteamGuardAccount account)
        {
            throw new InvalidOperationException("Import login should not have been required.");
        }
    }
}
