using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SDA.Core.Storage;
using SteamAuth;
using Xunit;

namespace SDA.Core.Tests
{
    public class MaFileSerializationTests
    {
        private const ulong FixtureSteamId = 76561198000000001;

        [Fact]
        public void PlainMaFile_PreservesLegacyPropertyNamesAndValues()
        {
            string json = FixtureFiles.Read("fixture-plain-mafile.json");

            Assert.Contains("\"shared_secret\"", json);
            Assert.Contains("\"identity_secret\"", json);
            Assert.Contains("\"revocation_code\"", json);
            Assert.Contains("\"device_id\"", json);
            Assert.Contains("\"fully_enrolled\"", json);
            Assert.Contains("\"Session\"", json);
            Assert.Contains("\"SteamID\"", json);
            Assert.DoesNotContain("\"SharedSecret\"", json);

            SteamGuardAccount account = JsonConvert.DeserializeObject<SteamGuardAccount>(json);
            AssertAccount(account);

            string serialized = JsonConvert.SerializeObject(account);
            SteamGuardAccount again = JsonConvert.DeserializeObject<SteamGuardAccount>(serialized);
            AssertAccount(again);
            Assert.Contains("\"shared_secret\"", serialized);
            Assert.Contains("\"Session\"", serialized);
        }

        [Fact]
        public void MissingFields_DeserializeAndSkipCodeGeneration()
        {
            SteamGuardAccount account = JsonConvert.DeserializeObject<SteamGuardAccount>(
                FixtureFiles.Read("fixture-missing-fields.json"));

            Assert.NotNull(account);
            Assert.Null(account.AccountName);
            Assert.Null(account.SharedSecret);
            Assert.Null(account.Session);
            Assert.False(account.FullyEnrolled);
            Assert.Equal("", account.GenerateSteamGuardCodeForTime(1600000000));
        }

        [Fact]
        public void InvalidJson_Throws()
        {
            Assert.Throws<JsonReaderException>(() =>
                JsonConvert.DeserializeObject<SteamGuardAccount>(FixtureFiles.Read("fixture-invalid.json")));
        }

        [Fact]
        public void EmptyFile_DeserializesToNull()
        {
            SteamGuardAccount account = JsonConvert.DeserializeObject<SteamGuardAccount>(
                FixtureFiles.Read("fixture-empty.json"));

            Assert.Null(account);
        }

        [Fact]
        public void UnsupportedContent_Throws()
        {
            Assert.Throws<JsonReaderException>(() =>
                JsonConvert.DeserializeObject<SteamGuardAccount>(FixtureFiles.Read("fixture-unsupported.json")));
        }

        [Fact]
        public void ImportManifest_PreservesEncryptionFieldNames()
        {
            string json = FixtureFiles.Read("fixture-import-manifest.json");
            ImportManifest manifest = JsonConvert.DeserializeObject<ImportManifest>(json);

            Assert.True(manifest.Encrypted);
            Assert.Equal("76561198000000001.maFile", manifest.Entries[0].Filename);
            Assert.Equal("ECEyQ1Rldoc=", manifest.Entries[0].Salt);
            Assert.Equal("ABEiM0RVZneImaq7zN3u/w==", manifest.Entries[0].IV);
            Assert.Equal(FixtureSteamId, manifest.Entries[0].SteamID);

            JObject serialized = JObject.Parse(JsonConvert.SerializeObject(manifest));
            Assert.NotNull(serialized["entries"][0]["encryption_salt"]);
            Assert.NotNull(serialized["entries"][0]["encryption_iv"]);
            Assert.NotNull(serialized["entries"][0]["steamid"]);
        }

        private static void AssertAccount(SteamGuardAccount account)
        {
            Assert.Equal("fixture_user", account.AccountName);
            Assert.Equal("c3ludGhldGljLXNoYXJlZC1zZWNyZXQ=", account.SharedSecret);
            Assert.Equal("c3ludGhldGljLWlkZW50aXR5LXNlY3JldA==", account.IdentitySecret);
            Assert.Equal("FIXTURE", account.RevocationCode);
            Assert.Equal("1", account.SerialNumber);
            Assert.Equal("otpauth://totp/Steam:fixture_user?secret=SYNTHETIC&issuer=Steam", account.URI);
            Assert.Equal(1600000000, account.ServerTime);
            Assert.Equal("fixture-token-gid", account.TokenGID);
            Assert.Equal("c3ludGhldGljLXNlY3JldC0x", account.Secret1);
            Assert.Equal(1, account.Status);
            Assert.Equal("android:00000000-0000-0000-0000-000000000001", account.DeviceID);
            Assert.True(account.FullyEnrolled);
            Assert.NotNull(account.Session);
            Assert.Equal(FixtureSteamId, account.Session.SteamID);
            Assert.Equal("synthetic-access-token", account.Session.AccessToken);
            Assert.Equal("synthetic-refresh-token", account.Session.RefreshToken);
            Assert.Equal("synthetic-session-id", account.Session.SessionID);
        }
    }
}
