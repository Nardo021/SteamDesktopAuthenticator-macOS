using SDA.Core.Storage;
using SteamAuth;
using System;
using System.IO;
using System.Linq;

namespace SDA.Desktop.Services
{
    public enum MaFilesLoadKind
    {
        Success,
        NoAccounts,
        PasswordRequired,
        InvalidPassword,
        InvalidManifest,
        UnableToLoad,
        UnlockCancelled
    }

    public sealed class MaFilesLoadResult
    {
        public MaFilesLoadResult(MaFilesLoadKind kind, SteamGuardAccount[] accounts, string statusText, bool rememberDirectory)
        {
            Kind = kind;
            Accounts = accounts ?? new SteamGuardAccount[0];
            StatusText = statusText ?? "";
            RememberDirectory = rememberDirectory;
        }

        public MaFilesLoadKind Kind { get; }

        public SteamGuardAccount[] Accounts { get; }

        public string StatusText { get; }

        public bool RememberDirectory { get; }
    }

    public sealed class AccountService
    {
        public const string UnableToLoadMessage = "Unable to load maFiles. Choose a maFiles folder or set up an account.";
        public const string NoAccountsMessage = "No accounts yet. Set up a new account, import a maFile, or open a maFiles folder.";
        public const string UnableToDecryptMessage = "Unable to decrypt accounts. Check the encryption passkey and try again.";
        public const string InvalidManifestMessage = "Invalid manifest";

        public bool LooksLikeMaFilesFolder(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            if (File.Exists(Path.Combine(directory, "manifest.json")))
            {
                return true;
            }

            return Directory.EnumerateFiles(directory, "*.maFile").Any();
        }

        public MaFilesLoadResult Load(string directory, string passKey)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return Unable(UnableToLoadMessage);
            }

            string manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                if (Directory.EnumerateFiles(directory, "*.maFile").Any())
                {
                    return new MaFilesLoadResult(MaFilesLoadKind.InvalidManifest, new SteamGuardAccount[0], InvalidManifestMessage, false);
                }

                return new MaFilesLoadResult(MaFilesLoadKind.NoAccounts, new SteamGuardAccount[0], NoAccountsMessage, false);
            }

            try
            {
                Manifest manifest = Manifest.GetManifest(directory);
                AccountLoadResult loaded = manifest.LoadAccounts(passKey);
                switch (loaded.Status)
                {
                    case AccountLoadStatus.PasswordRequired:
                        return new MaFilesLoadResult(MaFilesLoadKind.PasswordRequired, new SteamGuardAccount[0], "", true);
                    case AccountLoadStatus.InvalidPassword:
                        return new MaFilesLoadResult(MaFilesLoadKind.InvalidPassword, new SteamGuardAccount[0], UnableToDecryptMessage, true);
                    case AccountLoadStatus.NoAccounts:
                        return new MaFilesLoadResult(MaFilesLoadKind.NoAccounts, new SteamGuardAccount[0], NoAccountsMessage, true);
                    default:
                        return new MaFilesLoadResult(MaFilesLoadKind.Success, loaded.Accounts, "", true);
                }
            }
            catch (ManifestParseException)
            {
                return new MaFilesLoadResult(MaFilesLoadKind.InvalidManifest, new SteamGuardAccount[0], InvalidManifestMessage, false);
            }
            catch (Exception)
            {
                return Unable(UnableToLoadMessage);
            }
        }

        private static MaFilesLoadResult Unable(string status)
        {
            return new MaFilesLoadResult(MaFilesLoadKind.UnableToLoad, new SteamGuardAccount[0], status, false);
        }
    }
}
