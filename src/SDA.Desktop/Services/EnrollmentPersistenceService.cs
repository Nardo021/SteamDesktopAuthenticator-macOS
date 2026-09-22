using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;

namespace SDA.Desktop.Services
{
    public enum EnrollmentEncryptionKind
    {
        Unencrypted,
        AskNewPasskey,
        UseExistingKey,
        AskExistingKey
    }

    public sealed class EnrollmentEncryptionPlan
    {
        public EnrollmentEncryptionPlan(EnrollmentEncryptionKind kind, bool encrypt)
        {
            Kind = kind;
            Encrypt = encrypt;
        }

        public EnrollmentEncryptionKind Kind { get; }

        public bool Encrypt { get; }
    }

    public enum EnrollmentSaveStatus
    {
        Saved,
        Failed,
        InvalidKey
    }

    public sealed class EnrollmentSaveResult
    {
        public EnrollmentSaveResult(EnrollmentSaveStatus status)
        {
            Status = status;
        }

        public EnrollmentSaveStatus Status { get; }

        public static EnrollmentSaveResult Saved()
        {
            return new EnrollmentSaveResult(EnrollmentSaveStatus.Saved);
        }

        public static EnrollmentSaveResult Failed()
        {
            return new EnrollmentSaveResult(EnrollmentSaveStatus.Failed);
        }

        public static EnrollmentSaveResult InvalidKey()
        {
            return new EnrollmentSaveResult(EnrollmentSaveStatus.InvalidKey);
        }
    }

    public class EnrollmentPersistenceService
    {
        public virtual EnrollmentEncryptionPlan Inspect(string directory, string currentPassKey)
        {
            Manifest manifest = TryGetManifest(directory);
            if (manifest == null || manifest.Entries == null || manifest.Entries.Count == 0)
            {
                return new EnrollmentEncryptionPlan(EnrollmentEncryptionKind.AskNewPasskey, false);
            }

            if (!manifest.Encrypted)
            {
                return new EnrollmentEncryptionPlan(EnrollmentEncryptionKind.Unencrypted, false);
            }

            if (!string.IsNullOrEmpty(currentPassKey) && manifest.VerifyPasskey(currentPassKey))
            {
                return new EnrollmentEncryptionPlan(EnrollmentEncryptionKind.UseExistingKey, true);
            }

            return new EnrollmentEncryptionPlan(EnrollmentEncryptionKind.AskExistingKey, true);
        }

        public virtual EnrollmentSaveResult SaveAccount(SteamGuardAccount account, string directory, bool encrypt, string passKey)
        {
            return ManifestMutationGate.Shared.Run(() => SaveAccountUnlocked(account, directory, encrypt, passKey));
        }

        public virtual bool RemoveAccount(SteamGuardAccount account, string directory)
        {
            return ManifestMutationGate.Shared.Run(() => RemoveAccountUnlocked(account, directory));
        }

        private EnrollmentSaveResult SaveAccountUnlocked(SteamGuardAccount account, string directory, bool encrypt, string passKey)
        {
            if (account == null || string.IsNullOrEmpty(directory))
            {
                return EnrollmentSaveResult.Failed();
            }

            Manifest manifest = GetOrCreateManifest(directory);
            if (manifest == null)
            {
                return EnrollmentSaveResult.Failed();
            }

            if (encrypt)
            {
                if (string.IsNullOrEmpty(passKey))
                {
                    return EnrollmentSaveResult.Failed();
                }

                if (manifest.Encrypted && !manifest.VerifyPasskey(passKey))
                {
                    return EnrollmentSaveResult.InvalidKey();
                }
            }
            else if (manifest.Encrypted)
            {
                return EnrollmentSaveResult.Failed();
            }

            try
            {
                if (!manifest.SaveAccount(account, encrypt, encrypt ? passKey : null))
                {
                    return EnrollmentSaveResult.Failed();
                }

                return EnrollmentSaveResult.Saved();
            }
            catch (Exception)
            {
                return EnrollmentSaveResult.Failed();
            }
        }

        private bool RemoveAccountUnlocked(SteamGuardAccount account, string directory)
        {
            if (account == null || string.IsNullOrEmpty(directory))
            {
                return false;
            }

            Manifest manifest = TryGetManifest(directory);
            if (manifest == null)
            {
                return true;
            }

            try
            {
                return manifest.RemoveAccount(account);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public Manifest TryGetManifest(string directory)
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

        public Manifest GetOrCreateManifest(string directory)
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
    }
}
