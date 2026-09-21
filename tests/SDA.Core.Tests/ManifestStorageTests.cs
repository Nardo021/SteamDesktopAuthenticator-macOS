using Newtonsoft.Json;
using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;
using Xunit;

namespace SDA.Core.Tests
{
    public class ManifestStorageTests : IDisposable
    {
        private const ulong FixtureSteamId = 76561198000000001;
        private readonly string _root;

        public ManifestStorageTests()
        {
            _root = Directory.CreateTempSubdirectory("sda-core-").FullName;
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
        public void GetManifest_CreatesManifestInsideTheSuppliedDirectory()
        {
            string directory = NewDirectory();
            string other = NewDirectory();

            Manifest manifest = Manifest.GetManifest(directory);

            Assert.False(manifest.Encrypted);
            Assert.True(manifest.FirstRun);
            Assert.Empty(manifest.Entries);
            Assert.Equal(5, manifest.PeriodicCheckingInterval);
            Assert.True(File.Exists(Path.Combine(directory, "manifest.json")));
            Assert.False(File.Exists(Path.Combine(other, "manifest.json")));

            string json = File.ReadAllText(Path.Combine(directory, "manifest.json"));
            Assert.Contains("\"encrypted\"", json);
            Assert.Contains("\"first_run\"", json);
            Assert.Contains("\"periodic_checking_interval\"", json);
            Assert.Contains("\"periodic_checking_checkall\"", json);
            Assert.Contains("\"auto_confirm_market_transactions\"", json);
            Assert.Contains("\"auto_confirm_trades\"", json);
            Assert.DoesNotContain("_maFilesDirectory", json);
            Assert.DoesNotContain("GetExecutableDir", json);
        }

        [Fact]
        public void MissingManifest_ThrowsManifestParseException()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);

            Assert.Throws<ManifestParseException>(() => Manifest.GetManifest(directory));
        }

        [Fact]
        public void InvalidManifest_ThrowsManifestParseException()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), FixtureFiles.Read("fixture-invalid.json"));

            Assert.Throws<ManifestParseException>(() => Manifest.GetManifest(directory));
        }

        [Fact]
        public void EmptyManifest_ThrowsManifestParseException()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "");

            Assert.Throws<ManifestParseException>(() => Manifest.GetManifest(directory));
        }

        [Fact]
        public void PlainAccount_RoundTripsThroughTheSuppliedDirectory()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            SteamGuardAccount account = LoadPlainAccount();

            Assert.True(manifest.SaveAccount(account, false));

            string maFilePath = Path.Combine(directory, FixtureSteamId + ".maFile");
            string stored = File.ReadAllText(maFilePath);
            Assert.Contains("\"shared_secret\"", stored);
            Assert.Contains("\"account_name\"", stored);

            Manifest reloaded = Manifest.GetManifest(directory);
            SteamGuardAccount[] accounts = reloaded.GetAllAccounts();

            Assert.Single(accounts);
            Assert.Equal("fixture_user", accounts[0].AccountName);
            Assert.Equal(account.SharedSecret, accounts[0].SharedSecret);
            Assert.Equal(FixtureSteamId, accounts[0].Session.SteamID);
            Assert.Equal("synthetic-refresh-token", accounts[0].Session.RefreshToken);
        }

        [Fact]
        public void EncryptedAccount_RoundTripsAndHidesPlaintext()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            SteamGuardAccount account = LoadPlainAccount();

            Assert.True(manifest.SaveAccount(account, true, "fixture-pass"));

            string stored = File.ReadAllText(Path.Combine(directory, FixtureSteamId + ".maFile"));
            Assert.DoesNotContain("shared_secret", stored);
            Assert.DoesNotContain("fixture_user", stored);
            Assert.DoesNotContain("synthetic-refresh-token", stored);

            Manifest reloaded = Manifest.GetManifest(directory);
            Assert.True(reloaded.Encrypted);
            Assert.True(reloaded.VerifyPasskey("fixture-pass"));
            Assert.False(reloaded.VerifyPasskey("wrong-passkey"));

            SteamGuardAccount[] accounts = reloaded.GetAllAccounts("fixture-pass");
            Assert.Single(accounts);
            Assert.Equal(account.SharedSecret, accounts[0].SharedSecret);
            Assert.Empty(reloaded.GetAllAccounts("wrong-passkey"));
            Assert.Empty(reloaded.GetAllAccounts());
        }

        [Fact]
        public void IncorrectPasskey_DoesNotRewriteEncryptedFile()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.SaveAccount(LoadPlainAccount(), true, "fixture-pass"));

            string path = Path.Combine(directory, FixtureSteamId + ".maFile");
            string before = File.ReadAllText(path);

            Assert.False(manifest.ChangeEncryptionKey("wrong-passkey", "other-pass"));
            Assert.Equal(before, File.ReadAllText(path));
        }

        [Fact]
        public void ChangeEncryptionKey_CanRemoveEncryption()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            Assert.True(manifest.SaveAccount(LoadPlainAccount(), true, "fixture-pass"));

            Assert.True(manifest.ChangeEncryptionKey("fixture-pass", null));

            string stored = File.ReadAllText(Path.Combine(directory, FixtureSteamId + ".maFile"));
            Assert.Contains("\"shared_secret\"", stored);

            Manifest reloaded = Manifest.GetManifest(directory);
            Assert.False(reloaded.Encrypted);
            Assert.Equal("fixture_user", reloaded.GetAllAccounts()[0].AccountName);
        }

        [Fact]
        public void MalformedMaFile_ThrowsWhenLoadingAccounts()
        {
            string directory = WriteManifestWithFile("fixture-invalid.json");

            Manifest manifest = Manifest.GetManifest(directory);

            Assert.Throws<JsonReaderException>(() => manifest.GetAllAccounts());
        }

        [Fact]
        public void UnsupportedMaFile_ThrowsWhenLoadingAccounts()
        {
            string directory = WriteManifestWithFile("fixture-unsupported.json");

            Manifest manifest = Manifest.GetManifest(directory);

            Assert.Throws<JsonReaderException>(() => manifest.GetAllAccounts());
        }

        [Fact]
        public void EmptyMaFile_IsSkipped()
        {
            string directory = WriteManifestWithFile("fixture-empty.json");

            SteamGuardAccount[] accounts = Manifest.GetManifest(directory).GetAllAccounts();

            Assert.Empty(accounts);
        }

        [Fact]
        public void MissingSessionDuringScan_ThrowsMaFileEncryptedException()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "orphan.maFile"), FixtureFiles.Read("fixture-missing-fields.json"));

            Assert.Throws<MaFileEncryptedException>(() => Manifest.GenerateNewManifest(directory, true));
        }

        [Fact]
        public void PlainMaFileScan_CreatesAnEntryForThatDirectoryOnly()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.Copy(FixtureFiles.PathFor("fixture-plain-mafile.json"), Path.Combine(directory, "copied.maFile"));

            Manifest manifest = Manifest.GenerateNewManifest(directory, true);

            Assert.Single(manifest.Entries);
            Assert.Equal(FixtureSteamId, manifest.Entries[0].SteamID);
            Assert.Equal("copied.maFile", manifest.Entries[0].Filename);
            Assert.Equal("fixture_user", manifest.GetAllAccounts()[0].AccountName);
        }

        [Fact]
        public void MissingMaFile_DropsTheEntryWithoutWritingOutsideTheDirectory()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"),
                "{\"encrypted\":true,\"first_run\":false,\"entries\":[{\"encryption_iv\":null,\"encryption_salt\":null,\"filename\":\"missing.maFile\",\"steamid\":1}],\"periodic_checking\":false,\"periodic_checking_interval\":5,\"periodic_checking_checkall\":false,\"auto_confirm_market_transactions\":false,\"auto_confirm_trades\":false}");

            Manifest manifest = Manifest.GetManifest(directory);

            Assert.Empty(manifest.Entries);
            Assert.False(manifest.Encrypted);
        }

        [Fact]
        public void RemoveAccount_WithoutDeletingFile_KeepsTheMaFileAndDropsTheEntry()
        {
            string directory = NewDirectory();
            Manifest manifest = Manifest.GetManifest(directory);
            SteamGuardAccount account = LoadPlainAccount();
            Assert.True(manifest.SaveAccount(account, false));

            // Original RemoveAccount returns false when deleteMaFile is false, after the manifest save.
            Assert.False(manifest.RemoveAccount(account, false));

            string maFilePath = Path.Combine(directory, FixtureSteamId + ".maFile");
            Assert.True(File.Exists(maFilePath));
            Assert.Empty(Manifest.GetManifest(directory).Entries);
        }

        [Fact]
        public void EncryptedManifestWithNoEntries_ClearsTheEncryptedFlag()
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"),
                "{\"encrypted\":true,\"first_run\":false,\"entries\":[],\"periodic_checking\":true,\"periodic_checking_interval\":9,\"periodic_checking_checkall\":true,\"auto_confirm_market_transactions\":false,\"auto_confirm_trades\":false}");

            Manifest manifest = Manifest.GetManifest(directory);

            Assert.False(manifest.Encrypted);
            Assert.True(manifest.PeriodicChecking);
            Assert.Equal(9, manifest.PeriodicCheckingInterval);
            Assert.True(manifest.CheckAllAccounts);
            Assert.False(JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(Path.Combine(directory, "manifest.json"))).Encrypted);
        }

        private string WriteManifestWithFile(string fixtureName)
        {
            string directory = NewDirectory();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, FixtureSteamId + ".maFile"), FixtureFiles.Read(fixtureName));
            File.WriteAllText(Path.Combine(directory, "manifest.json"),
                "{\"encrypted\":false,\"first_run\":false,\"entries\":[{\"encryption_iv\":null,\"encryption_salt\":null,\"filename\":\"" + FixtureSteamId + ".maFile\",\"steamid\":" + FixtureSteamId + "}],\"periodic_checking\":false,\"periodic_checking_interval\":5,\"periodic_checking_checkall\":false,\"auto_confirm_market_transactions\":false,\"auto_confirm_trades\":false}");
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
