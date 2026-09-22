using Newtonsoft.Json;
using SDA.Core.Encryption;
using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;

namespace SDA.Desktop.Tests
{
    internal static class Phase6Fixtures
    {
        public const ulong SteamId = 76561198000000077;
        public const string AccountName = "fixture_import";
        public const string SharedSecret = "c3ludGhldGljLXBoYXNlNi1zaGFyZWQ=";
        public const string IdentitySecret = "c3ludGhldGljLXBoYXNlNi1pZGVudA==";

        public static SteamGuardAccount Account(string accessToken, string refreshToken, ulong steamId = SteamId)
        {
            return new SteamGuardAccount
            {
                AccountName = AccountName,
                SharedSecret = SharedSecret,
                IdentitySecret = IdentitySecret,
                RevocationCode = "R00000",
                DeviceID = "android:00000000-0000-0000-0000-000000000077",
                FullyEnrolled = true,
                Session = new SessionData
                {
                    SteamID = steamId,
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    SessionID = "synthetic-session-id"
                }
            };
        }

        public static SteamGuardAccount ValidAccount()
        {
            return Account(SyntheticJwt.Valid(), SyntheticJwt.Valid());
        }

        public static SteamGuardAccount ExpiredAccessAccount()
        {
            return Account(SyntheticJwt.Expired(), SyntheticJwt.Valid());
        }

        public static SteamGuardAccount ExpiredRefreshAccount()
        {
            return Account(SyntheticJwt.Valid(), SyntheticJwt.Expired());
        }

        public static SteamGuardAccount MissingSessionAccount()
        {
            SteamGuardAccount account = ValidAccount();
            account.Session = null;
            return account;
        }

        public static string WriteMaFile(string directory, SteamGuardAccount account)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, (account.Session == null ? SteamId : account.Session.SteamID) + ".maFile");
            File.WriteAllText(path, JsonConvert.SerializeObject(account));
            return path;
        }

        public static string WriteEncryptedMaFile(string directory, SteamGuardAccount account, string passKey, string fileName)
        {
            Directory.CreateDirectory(directory);
            string salt = FileEncryptor.GetRandomSalt();
            string iv = FileEncryptor.GetInitializationVector();
            string json = JsonConvert.SerializeObject(account);
            string encrypted = FileEncryptor.EncryptData(passKey, salt, iv, json);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, encrypted);
            ImportManifest manifest = new ImportManifest
            {
                Encrypted = true,
                Entries = new System.Collections.Generic.List<ImportManifestEntry>
                {
                    new ImportManifestEntry
                    {
                        Filename = fileName,
                        Salt = salt,
                        IV = iv,
                        SteamID = account.Session == null ? SteamId : account.Session.SteamID
                    }
                }
            };
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonConvert.SerializeObject(manifest));
            return path;
        }

        public static Manifest Destination(string directory, bool encrypt, string passKey, params SteamGuardAccount[] accounts)
        {
            Directory.CreateDirectory(directory);
            Manifest manifest = Manifest.GenerateNewManifest(directory, false);
            for (int i = 0; i < accounts.Length; i++)
            {
                AssertTrue(manifest.SaveAccount(accounts[i], encrypt, encrypt ? passKey : null));
            }

            return Manifest.GetManifest(directory);
        }

        public static SteamGuardAccount LoadPlainFixture()
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fixture-plain-mafile.json");
            return JsonConvert.DeserializeObject<SteamGuardAccount>(File.ReadAllText(fixture));
        }

        private static void AssertTrue(bool value)
        {
            if (!value)
            {
                throw new InvalidOperationException("Unable to write destination account.");
            }
        }
    }
}
