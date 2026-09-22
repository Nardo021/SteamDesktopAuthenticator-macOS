using Newtonsoft.Json;
using SDA.Core.Encryption;
using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public enum AccountImportStatus
    {
        Saved,
        DestinationEncrypted,
        InvalidMaFile,
        MissingAdjacentManifest,
        InvalidAdjacentManifest,
        EntryNotFound,
        MissingSalt,
        MissingIv,
        MissingSaltAndIv,
        DecryptionFailed,
        LoginRequired,
        LoginCancelled,
        LoginFailed,
        SaveFailed,
        Cancelled,
        Replaced
    }

    public sealed class AccountImportResult
    {
        public AccountImportResult(AccountImportStatus status, SteamGuardAccount account, string message)
        {
            Status = status;
            Account = account;
            Message = message ?? "";
        }

        public AccountImportStatus Status { get; }

        public SteamGuardAccount Account { get; }

        public string Message { get; }

        public bool Succeeded
        {
            get { return Status == AccountImportStatus.Saved || Status == AccountImportStatus.Replaced; }
        }
    }

    public sealed class AccountImportReadResult
    {
        public AccountImportReadResult(AccountImportStatus status, SteamGuardAccount account, string message)
        {
            Status = status;
            Account = account;
            Message = message ?? "";
        }

        public AccountImportStatus Status { get; }

        public SteamGuardAccount Account { get; }

        public string Message { get; }

        public bool NeedsLogin
        {
            get { return Status == AccountImportStatus.LoginRequired; }
        }
    }

    public class AccountImportService
    {
        public const string DestinationEncryptedMessage = "You can't import an .maFile because the existing accounts in SDA are encrypted. Decrypt the current Manifest and try again.";
        public const string InvalidMaFileMessage = "This file is not a valid SteamAuth maFile. Import Failed.";
        public const string MissingAdjacentManifestMessage = "manifest.json is missing! Import Failed.";
        public const string InvalidAdjacentManifestMessage = "Invalid content inside manifest.json! Import Failed.";
        public const string EntryNotFoundMessage = "Account not found inside manifest.json. Import Failed.";
        public const string MissingSaltAndIvMessage = "manifest.json does not contain encrypted data. Your account may be unencrypted! Import Failed.";
        public const string MissingIvMessage = "manifest.json does not contain: encryption_iv Import Failed.";
        public const string MissingSaltMessage = "manifest.json does not contain: encryption_salt Import Failed.";
        public const string DecryptionFailedMessage = "Decryption Failed. Import Failed.";
        public const string LoginFailedMessage = "Login failed. Try to import this account again.";
        public const string SuccessMessage = "Account Imported";
        public const string EncryptedSourceSuccessMessage = "Account Imported. Your Account is now Decrypted.";

        public bool IsDestinationEncrypted(string destinationDirectory)
        {
            Manifest manifest = TryGetManifest(destinationDirectory);
            return manifest != null && manifest.Encrypted;
        }

        public bool NeedsImportLogin(SteamGuardAccount account)
        {
            if (account == null || account.Session == null || account.Session.SteamID == 0)
            {
                return true;
            }

            try
            {
                return account.Session.IsAccessTokenExpired();
            }
            catch (Exception)
            {
                return true;
            }
        }

        public bool DestinationContainsSteamId(string destinationDirectory, ulong steamId)
        {
            Manifest manifest = TryGetManifest(destinationDirectory);
            if (manifest == null || manifest.Entries == null)
            {
                return false;
            }

            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                if (manifest.Entries[i].SteamID == steamId)
                {
                    return true;
                }
            }

            return false;
        }

        public AccountImportReadResult ReadSource(string maFilePath, string sourcePassKey)
        {
            if (string.IsNullOrEmpty(maFilePath) || !File.Exists(maFilePath))
            {
                return new AccountImportReadResult(AccountImportStatus.InvalidMaFile, null, InvalidMaFileMessage);
            }

            string fileContents;
            try
            {
                fileContents = File.ReadAllText(maFilePath);
            }
            catch (Exception)
            {
                return new AccountImportReadResult(AccountImportStatus.InvalidMaFile, null, InvalidMaFileMessage);
            }

            if (string.IsNullOrEmpty(sourcePassKey))
            {
                return ReadPlainAccount(fileContents);
            }

            return ReadEncryptedAccount(maFilePath, fileContents, sourcePassKey);
        }

        public async Task<AccountImportResult> ImportAsync(
            string maFilePath,
            string sourcePassKey,
            string destinationDirectory,
            Func<SteamGuardAccount, Task<SessionData>> importLogin,
            Func<ulong, Task<bool>> confirmReplace)
        {
            if (IsDestinationEncrypted(destinationDirectory))
            {
                return Failed(AccountImportStatus.DestinationEncrypted, DestinationEncryptedMessage);
            }

            AccountImportReadResult read = ReadSource(maFilePath, sourcePassKey);
            if (read.Status != AccountImportStatus.Saved && read.Status != AccountImportStatus.LoginRequired)
            {
                return Failed(read.Status, read.Message);
            }

            SteamGuardAccount account = read.Account;
            if (account == null || !IsPlausibleMaFile(account))
            {
                return Failed(AccountImportStatus.InvalidMaFile, InvalidMaFileMessage);
            }

            if (NeedsImportLogin(account))
            {
                if (importLogin == null)
                {
                    return Failed(AccountImportStatus.LoginFailed, LoginFailedMessage);
                }

                SessionData session;
                try
                {
                    session = await importLogin(account);
                }
                catch (OperationCanceledException)
                {
                    return Failed(AccountImportStatus.LoginCancelled, LoginFailedMessage);
                }

                if (session == null)
                {
                    return Failed(AccountImportStatus.LoginCancelled, LoginFailedMessage);
                }

                if (session.SteamID == 0)
                {
                    return Failed(AccountImportStatus.LoginFailed, LoginFailedMessage);
                }

                account.Session = session;
            }

            if (account.Session == null || account.Session.SteamID == 0)
            {
                return Failed(AccountImportStatus.LoginFailed, LoginFailedMessage);
            }

            bool replacing = DestinationContainsSteamId(destinationDirectory, account.Session.SteamID);
            if (replacing && confirmReplace != null && !await confirmReplace(account.Session.SteamID))
            {
                return Failed(AccountImportStatus.Cancelled, "");
            }

            return ManifestMutationGate.Shared.Run(() => SaveImported(account, destinationDirectory, replacing, !string.IsNullOrEmpty(sourcePassKey)));
        }

        private AccountImportResult SaveImported(SteamGuardAccount account, string destinationDirectory, bool replacing, bool fromEncryptedSource)
        {
            if (IsDestinationEncrypted(destinationDirectory))
            {
                return Failed(AccountImportStatus.DestinationEncrypted, DestinationEncryptedMessage);
            }

            Manifest manifest = GetOrCreateManifest(destinationDirectory);
            if (manifest == null)
            {
                return Failed(AccountImportStatus.SaveFailed, InvalidMaFileMessage);
            }

            MaFilesDirectorySnapshot snapshot = MaFilesDirectorySnapshot.Capture(destinationDirectory);
            try
            {
                if (!manifest.SaveAccount(account, false))
                {
                    snapshot.Restore();
                    return Failed(AccountImportStatus.SaveFailed, InvalidMaFileMessage);
                }
            }
            catch (Exception)
            {
                snapshot.Restore();
                return Failed(AccountImportStatus.SaveFailed, InvalidMaFileMessage);
            }

            string message = fromEncryptedSource ? EncryptedSourceSuccessMessage : SuccessMessage;
            return new AccountImportResult(replacing ? AccountImportStatus.Replaced : AccountImportStatus.Saved, account, message);
        }

        private AccountImportReadResult ReadPlainAccount(string fileContents)
        {
            SteamGuardAccount account = DeserializeAccount(fileContents);
            if (account == null || !IsPlausibleMaFile(account))
            {
                return new AccountImportReadResult(AccountImportStatus.InvalidMaFile, null, InvalidMaFileMessage);
            }

            if (NeedsImportLogin(account))
            {
                return new AccountImportReadResult(AccountImportStatus.LoginRequired, account, "");
            }

            return new AccountImportReadResult(AccountImportStatus.Saved, account, "");
        }

        private AccountImportReadResult ReadEncryptedAccount(string maFilePath, string fileContents, string sourcePassKey)
        {
            string directory = Path.GetDirectoryName(maFilePath);
            string fileName = Path.GetFileName(maFilePath);
            if (string.IsNullOrEmpty(directory))
            {
                return new AccountImportReadResult(AccountImportStatus.MissingAdjacentManifest, null, MissingAdjacentManifestMessage);
            }

            string manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return new AccountImportReadResult(AccountImportStatus.MissingAdjacentManifest, null, MissingAdjacentManifestMessage);
            }

            ImportManifest sourceManifest;
            try
            {
                sourceManifest = JsonConvert.DeserializeObject<ImportManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception)
            {
                return new AccountImportReadResult(AccountImportStatus.InvalidAdjacentManifest, null, InvalidAdjacentManifestMessage);
            }

            if (sourceManifest == null || sourceManifest.Entries == null)
            {
                return new AccountImportReadResult(AccountImportStatus.InvalidAdjacentManifest, null, InvalidAdjacentManifestMessage);
            }

            ImportManifestEntry match = null;
            for (int i = 0; i < sourceManifest.Entries.Count; i++)
            {
                ImportManifestEntry entry = sourceManifest.Entries[i];
                if (entry != null && string.Equals(entry.Filename, fileName, StringComparison.Ordinal))
                {
                    match = entry;
                    break;
                }
            }

            if (match == null)
            {
                return new AccountImportReadResult(AccountImportStatus.EntryNotFound, null, EntryNotFoundMessage);
            }

            bool missingSalt = string.IsNullOrEmpty(match.Salt);
            bool missingIv = string.IsNullOrEmpty(match.IV);
            if (missingSalt && missingIv)
            {
                return new AccountImportReadResult(AccountImportStatus.MissingSaltAndIv, null, MissingSaltAndIvMessage);
            }

            if (missingIv)
            {
                return new AccountImportReadResult(AccountImportStatus.MissingIv, null, MissingIvMessage);
            }

            if (missingSalt)
            {
                return new AccountImportReadResult(AccountImportStatus.MissingSalt, null, MissingSaltMessage);
            }

            string decrypted;
            try
            {
                decrypted = FileEncryptor.DecryptData(sourcePassKey, match.Salt, match.IV, fileContents);
            }
            catch (Exception)
            {
                return new AccountImportReadResult(AccountImportStatus.DecryptionFailed, null, DecryptionFailedMessage);
            }

            if (decrypted == null)
            {
                return new AccountImportReadResult(AccountImportStatus.DecryptionFailed, null, DecryptionFailedMessage);
            }

            return ReadPlainAccount(decrypted);
        }

        private static SteamGuardAccount DeserializeAccount(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<SteamGuardAccount>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsPlausibleMaFile(SteamGuardAccount account)
        {
            if (account == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(account.SharedSecret)
                || !string.IsNullOrEmpty(account.AccountName)
                || (account.Session != null && account.Session.SteamID != 0);
        }

        private static Manifest TryGetManifest(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            string manifestFile = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestFile))
            {
                return null;
            }

            try
            {
                return Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Manifest GetOrCreateManifest(string directory)
        {
            Manifest existing = TryGetManifest(directory);
            if (existing != null)
            {
                return existing;
            }

            try
            {
                return Manifest.GenerateNewManifest(directory, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static AccountImportResult Failed(AccountImportStatus status, string message)
        {
            return new AccountImportResult(status, null, message);
        }
    }
}
