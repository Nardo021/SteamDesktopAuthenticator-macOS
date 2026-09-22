using SteamAuth;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface ISteamLoginService
    {
        Task<SteamLoginResult> LoginAgainAsync(
            SteamGuardAccount account,
            string password,
            IAuthenticator challenges,
            ILoginStatus progress,
            CancellationToken cancellationToken);
    }

    public interface ILoginStatus
    {
        void Report(string status);
    }

    public sealed class SteamLoginResult
    {
        private SteamLoginResult(bool succeeded, bool cancelled, string error, SessionData session)
        {
            Succeeded = succeeded;
            Cancelled = cancelled;
            Error = error ?? "";
            Session = session;
        }

        public bool Succeeded { get; }

        public bool Cancelled { get; }

        public string Error { get; }

        public SessionData Session { get; }

        public static SteamLoginResult Success(SessionData session)
        {
            return new SteamLoginResult(true, false, "", session);
        }

        public static SteamLoginResult Fail(string error)
        {
            return new SteamLoginResult(false, false, string.IsNullOrEmpty(error) ? "Steam login failed." : error, null);
        }

        public static SteamLoginResult Cancel()
        {
            return new SteamLoginResult(false, true, "", null);
        }
    }

    public sealed class SteamLoginService : ISteamLoginService
    {
        private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);

        public async Task<SteamLoginResult> LoginAgainAsync(
            SteamGuardAccount account,
            string password,
            IAuthenticator challenges,
            ILoginStatus progress,
            CancellationToken cancellationToken)
        {
            if (account == null || string.IsNullOrEmpty(account.AccountName) || string.IsNullOrEmpty(password))
            {
                return SteamLoginResult.Fail("Steam login failed.");
            }

            SteamClient steamClient = new SteamClient();
            try
            {
                Report(progress, "Connecting to Steam...");
                steamClient.Connect();
                if (!await WaitForConnectionAsync(steamClient, cancellationToken))
                {
                    return SteamLoginResult.Fail("Steam connection failure.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, "Logging in...");
                CredentialsAuthSession authSession = await steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
                {
                    Username = account.AccountName,
                    Password = password,
                    IsPersistentSession = false,
                    PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_MobileApp,
                    ClientOSType = EOSType.Android9,
                    Authenticator = challenges,
                });

                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, "Waiting for Steam authentication...");
                AuthPollResult pollResponse = await authSession.PollingWaitForResultAsync(cancellationToken);
                SessionData sessionData = new SessionData
                {
                    SteamID = authSession.SteamID.ConvertToUInt64(),
                    AccessToken = pollResponse.AccessToken,
                    RefreshToken = pollResponse.RefreshToken,
                };
                return SteamLoginResult.Success(sessionData);
            }
            catch (OperationCanceledException)
            {
                return SteamLoginResult.Cancel();
            }
            catch (InvalidOperationException ex) when (ex.Message == "Account does not contain a valid authenticator")
            {
                return SteamLoginResult.Fail("Account does not contain a valid authenticator");
            }
            catch (Exception)
            {
                return SteamLoginResult.Fail("Steam login failed.");
            }
            finally
            {
                try
                {
                    steamClient.Disconnect();
                }
                catch (Exception)
                {
                }
            }
        }

        private static void Report(ILoginStatus progress, string status)
        {
            if (progress != null)
            {
                progress.Report(status);
            }
        }

        private static async Task<bool> WaitForConnectionAsync(SteamClient steamClient, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(ConnectionTimeout);
            while (!steamClient.IsConnected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                await Task.Delay(500, cancellationToken);
            }

            return true;
        }
    }
}
