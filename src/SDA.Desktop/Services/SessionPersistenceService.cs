using SDA.Core.Storage;
using SteamAuth;
using System;

namespace SDA.Desktop.Services
{
    public enum SessionSaveStatus
    {
        Saved,
        KeyRequired,
        InvalidKey,
        Failed
    }

    public sealed class SessionSaveResult
    {
        private SessionSaveResult(SessionSaveStatus status)
        {
            Status = status;
        }

        public SessionSaveStatus Status { get; }

        public static SessionSaveResult Saved()
        {
            return new SessionSaveResult(SessionSaveStatus.Saved);
        }

        public static SessionSaveResult KeyRequired()
        {
            return new SessionSaveResult(SessionSaveStatus.KeyRequired);
        }

        public static SessionSaveResult InvalidKey()
        {
            return new SessionSaveResult(SessionSaveStatus.InvalidKey);
        }

        public static SessionSaveResult Failed()
        {
            return new SessionSaveResult(SessionSaveStatus.Failed);
        }
    }

    public sealed class SessionPersistenceService
    {
        public SessionSaveResult Commit(SteamGuardAccount account, SessionData newSession, string directory, string passKey, bool markFullyEnrolled)
        {
            if (account == null || newSession == null || string.IsNullOrEmpty(directory))
            {
                return SessionSaveResult.Failed();
            }

            Manifest manifest;
            try
            {
                manifest = Manifest.GetManifest(directory);
            }
            catch (Exception)
            {
                return SessionSaveResult.Failed();
            }

            bool encrypt = manifest.Encrypted;
            if (encrypt)
            {
                if (string.IsNullOrEmpty(passKey))
                {
                    return SessionSaveResult.KeyRequired();
                }

                if (!manifest.VerifyPasskey(passKey))
                {
                    return SessionSaveResult.InvalidKey();
                }
            }

            return ManifestMutationGate.Shared.Run(() =>
            {
                MaFilesDirectorySnapshot backup = MaFilesDirectorySnapshot.Capture(directory);
                SessionData previousSession = Clone(account.Session);
                bool previousEnrolled = account.FullyEnrolled;
                account.Session = newSession;
                if (markFullyEnrolled)
                {
                    account.FullyEnrolled = true;
                }

                bool saved;
                try
                {
                    saved = manifest.SaveAccount(account, encrypt, encrypt ? passKey : null);
                }
                catch (Exception)
                {
                    saved = false;
                }

                if (!saved)
                {
                    backup.Restore();
                    account.Session = previousSession;
                    account.FullyEnrolled = previousEnrolled;
                    return SessionSaveResult.Failed();
                }

                return SessionSaveResult.Saved();
            });
        }

        public static SessionData Clone(SessionData session)
        {
            if (session == null)
            {
                return null;
            }

            return new SessionData
            {
                SteamID = session.SteamID,
                AccessToken = session.AccessToken,
                RefreshToken = session.RefreshToken,
                SessionID = session.SessionID,
            };
        }
    }
}
