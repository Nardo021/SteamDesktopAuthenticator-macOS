using SteamAuth;

namespace SDA.Desktop.Services
{
    public enum SessionTokenState
    {
        Missing,
        RefreshTokenExpired,
        AccessTokenExpired,
        Valid
    }

    public static class SessionStateInspector
    {
        public static SessionTokenState Inspect(SessionData session)
        {
            if (session == null || string.IsNullOrEmpty(session.RefreshToken))
            {
                return SessionTokenState.Missing;
            }

            try
            {
                if (session.IsRefreshTokenExpired())
                {
                    return SessionTokenState.RefreshTokenExpired;
                }
            }
            catch (System.Exception)
            {
                return SessionTokenState.RefreshTokenExpired;
            }

            try
            {
                if (session.IsAccessTokenExpired())
                {
                    return SessionTokenState.AccessTokenExpired;
                }
            }
            catch (System.Exception)
            {
                return SessionTokenState.AccessTokenExpired;
            }

            return SessionTokenState.Valid;
        }

        public static bool CanForceRefresh(SessionData session)
        {
            SessionTokenState state = Inspect(session);
            return state == SessionTokenState.Valid || state == SessionTokenState.AccessTokenExpired;
        }
    }
}
