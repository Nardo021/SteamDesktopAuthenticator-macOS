using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;

namespace SDA.Desktop.Services
{
    public enum ManifestRemovalStatus
    {
        Removed,
        EncryptedBlocked,
        Cancelled,
        Failed,
        AccountMissing
    }

    public sealed class ManifestRemovalResult
    {
        public ManifestRemovalResult(ManifestRemovalStatus status, string message, bool maFileExists)
        {
            Status = status;
            Message = message ?? "";
            MaFileExists = maFileExists;
        }

        public ManifestRemovalStatus Status { get; }

        public string Message { get; }

        public bool MaFileExists { get; }
    }

    public class ManifestAccountRemovalService
    {
        public const string EncryptedBlockedMessage = "You cannot remove accounts from the manifest file while it is encrypted.";
        public const string ConfirmationMessage = "This will remove the selected account from the manifest file. Use this to move a maFile to another computer. This will NOT delete your maFile.";
        public const string SuccessMessage = "Account removed from manifest. You can now move its maFile to another computer and import it using the File menu.";
        public const string FailedMessage = "Unable to remove the account from the manifest.";

        public ManifestRemovalResult RemoveFromManifest(SteamGuardAccount account, string directory)
        {
            if (account == null || account.Session == null || string.IsNullOrEmpty(directory))
            {
                return new ManifestRemovalResult(ManifestRemovalStatus.AccountMissing, FailedMessage, false);
            }

            return ManifestMutationGate.Shared.Run(() => RemoveUnlocked(account, directory));
        }

        private ManifestRemovalResult RemoveUnlocked(SteamGuardAccount account, string directory)
        {
            Manifest manifest;
            try
            {
                manifest = Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return new ManifestRemovalResult(ManifestRemovalStatus.Failed, FailedMessage, FileStillExists(directory, account));
            }

            if (manifest.Encrypted)
            {
                return new ManifestRemovalResult(ManifestRemovalStatus.EncryptedBlocked, EncryptedBlockedMessage, FileStillExists(directory, account));
            }

            string maFilePath = ExpectedMaFilePath(directory, account);
            bool existedBefore = !string.IsNullOrEmpty(maFilePath) && File.Exists(maFilePath);

            try
            {
                manifest.RemoveAccount(account, false);
            }
            catch (Exception)
            {
                return new ManifestRemovalResult(ManifestRemovalStatus.Failed, FailedMessage, existedBefore);
            }

            Manifest reloaded;
            try
            {
                reloaded = Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return new ManifestRemovalResult(ManifestRemovalStatus.Failed, FailedMessage, File.Exists(maFilePath));
            }

            if (EntryExists(reloaded, account.Session.SteamID))
            {
                return new ManifestRemovalResult(ManifestRemovalStatus.Failed, FailedMessage, File.Exists(maFilePath));
            }

            bool maFileExists = File.Exists(maFilePath);
            if (existedBefore && !maFileExists)
            {
                return new ManifestRemovalResult(ManifestRemovalStatus.Failed, FailedMessage, false);
            }

            return new ManifestRemovalResult(ManifestRemovalStatus.Removed, SuccessMessage, maFileExists);
        }

        private static bool EntryExists(Manifest manifest, ulong steamId)
        {
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

        private static string ExpectedMaFilePath(string directory, SteamGuardAccount account)
        {
            if (account == null || account.Session == null)
            {
                return null;
            }

            return Path.Combine(directory, account.Session.SteamID + ".maFile");
        }

        private static bool FileStillExists(string directory, SteamGuardAccount account)
        {
            string path = ExpectedMaFilePath(directory, account);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }
    }
}
