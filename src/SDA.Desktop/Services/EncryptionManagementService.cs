using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;

namespace SDA.Desktop.Services
{
    public enum EncryptionManagementKind
    {
        Empty,
        Setup,
        Manage
    }

    public enum EncryptionChangeStatus
    {
        Enabled,
        Changed,
        Removed,
        Unchanged,
        WrongCurrentKey,
        Mismatch,
        Empty,
        Failed
    }

    public sealed class EncryptionChangeResult
    {
        public EncryptionChangeResult(EncryptionChangeStatus status, string activePassKey, string message)
        {
            Status = status;
            ActivePassKey = activePassKey;
            Message = message ?? "";
        }

        public EncryptionChangeStatus Status { get; }

        public string ActivePassKey { get; }

        public string Message { get; }

        public bool Succeeded
        {
            get
            {
                return Status == EncryptionChangeStatus.Enabled
                    || Status == EncryptionChangeStatus.Changed
                    || Status == EncryptionChangeStatus.Removed;
            }
        }
    }

    public class EncryptionManagementService
    {
        public const string PasskeysDoNotMatchMessage = "Passkeys do not match. Enter the same new passkey in both fields.";
        public const string UnableToChangeMessage = "Unable to change passkey. Check the current passkey and try again.";
        public const string UnableToRemoveMessage = "Unable to remove passkey. Check the current passkey and try again.";
        public const string UnableToSetMessage = "Unable to set passkey. Check the new passkey and try again.";
        public const string WrongCurrentKeyMessage = "That passkey is incorrect. Enter the same encryption passkey used for your other accounts.";
        public const string EmptyManifestMessage = "Encryption cannot be changed while there are no accounts.";
        public const string RequiredMessage = "Enter your encryption passkey.";

        public EncryptionManagementKind Inspect(string directory)
        {
            Manifest manifest = TryGetManifest(directory);
            if (manifest == null || manifest.Entries == null || manifest.Entries.Count == 0)
            {
                return EncryptionManagementKind.Empty;
            }

            return manifest.Encrypted ? EncryptionManagementKind.Manage : EncryptionManagementKind.Setup;
        }

        public EncryptionChangeResult Change(string directory, string currentPassKey, string newPassKey, string confirmPassKey)
        {
            Manifest manifest = TryGetManifest(directory);
            if (manifest == null || manifest.Entries == null || manifest.Entries.Count == 0)
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.Empty, currentPassKey, EmptyManifestMessage);
            }

            string newKey = string.IsNullOrEmpty(newPassKey) ? null : newPassKey;
            string confirm = string.IsNullOrEmpty(confirmPassKey) ? null : confirmPassKey;
            if (!string.Equals(newKey, confirm, StringComparison.Ordinal))
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.Mismatch, currentPassKey, PasskeysDoNotMatchMessage);
            }

            if (!manifest.Encrypted && newKey == null)
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.Unchanged, null, "");
            }

            if (manifest.Encrypted && !manifest.VerifyPasskey(currentPassKey))
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.WrongCurrentKey, currentPassKey, WrongCurrentKeyMessage);
            }

            return ManifestMutationGate.Shared.Run(() => ApplyChange(directory, currentPassKey, newKey));
        }

        private EncryptionChangeResult ApplyChange(string directory, string currentPassKey, string newKey)
        {
            Manifest manifest;
            try
            {
                manifest = Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.Failed, currentPassKey, UnableToChangeMessage);
            }

            if (manifest.Entries == null || manifest.Entries.Count == 0)
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.Empty, currentPassKey, EmptyManifestMessage);
            }

            bool wasEncrypted = manifest.Encrypted;
            if (wasEncrypted && !manifest.VerifyPasskey(currentPassKey))
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.WrongCurrentKey, currentPassKey, WrongCurrentKeyMessage);
            }

            MaFilesDirectorySnapshot snapshot = MaFilesDirectorySnapshot.Capture(directory);
            bool changed;
            try
            {
                changed = manifest.ChangeEncryptionKey(wasEncrypted ? currentPassKey : null, newKey);
            }
            catch (Exception)
            {
                snapshot.Restore();
                return new EncryptionChangeResult(EncryptionChangeStatus.Failed, currentPassKey, FailureMessage(wasEncrypted, newKey));
            }

            if (!changed)
            {
                snapshot.Restore();
                return new EncryptionChangeResult(EncryptionChangeStatus.Failed, currentPassKey, FailureMessage(wasEncrypted, newKey));
            }

            if (!AccountsReadable(directory, newKey))
            {
                snapshot.Restore();
                return new EncryptionChangeResult(EncryptionChangeStatus.Failed, currentPassKey, FailureMessage(wasEncrypted, newKey));
            }

            if (newKey == null)
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.Removed, null, "Passkey successfully removed.");
            }

            if (!wasEncrypted)
            {
                return new EncryptionChangeResult(EncryptionChangeStatus.Enabled, newKey, "Passkey successfully set.");
            }

            return new EncryptionChangeResult(EncryptionChangeStatus.Changed, newKey, "Passkey successfully changed.");
        }

        private static bool AccountsReadable(string directory, string passKey)
        {
            try
            {
                Manifest manifest = Manifest.GetManifest(directory);
                AccountLoadResult loaded = manifest.LoadAccounts(passKey);
                if (loaded.Status != AccountLoadStatus.Success || loaded.Accounts == null || loaded.Accounts.Length == 0)
                {
                    return false;
                }

                for (int i = 0; i < loaded.Accounts.Length; i++)
                {
                    SteamGuardAccount account = loaded.Accounts[i];
                    if (account == null || string.IsNullOrEmpty(account.SharedSecret))
                    {
                        return false;
                    }
                }

                if (manifest.Encrypted)
                {
                    foreach (string path in Directory.GetFiles(directory, "*.maFile"))
                    {
                        string text = File.ReadAllText(path).TrimStart();
                        if (text.StartsWith("{", StringComparison.Ordinal))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string FailureMessage(bool wasEncrypted, string newKey)
        {
            if (!wasEncrypted)
            {
                return UnableToSetMessage;
            }

            return newKey == null ? UnableToRemoveMessage : UnableToChangeMessage;
        }

        private static Manifest TryGetManifest(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !File.Exists(Path.Combine(directory, "manifest.json")))
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
    }
}
