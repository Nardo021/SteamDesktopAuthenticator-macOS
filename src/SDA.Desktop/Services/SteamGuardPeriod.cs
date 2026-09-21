namespace SDA.Desktop.Services
{
    public static class SteamGuardPeriod
    {
        public const int PeriodSeconds = 30;

        public static int SecondsRemaining(long steamTime)
        {
            if (steamTime < 0)
            {
                steamTime = 0;
            }

            int elapsed = (int)(steamTime % PeriodSeconds);
            return PeriodSeconds - elapsed;
        }
    }

    public sealed class SteamGuardDisplayState
    {
        public SteamGuardDisplayState(string code, int secondsRemaining, bool canCopy, string statusText)
        {
            Code = code ?? "";
            SecondsRemaining = secondsRemaining;
            CanCopy = canCopy;
            StatusText = statusText;
        }

        public string Code { get; }

        public int SecondsRemaining { get; }

        public bool CanCopy { get; }

        public string StatusText { get; }
    }

    public static class SteamGuardDisplay
    {
        public static SteamGuardDisplayState ForAccount(SteamAuth.SteamGuardAccount account, long steamTime)
        {
            int secondsRemaining = SteamGuardPeriod.SecondsRemaining(steamTime);
            if (account == null || steamTime < 0)
            {
                return new SteamGuardDisplayState("", secondsRemaining, false, null);
            }

            try
            {
                string code = account.GenerateSteamGuardCodeForTime(steamTime);
                if (string.IsNullOrEmpty(code))
                {
                    return Unavailable(secondsRemaining);
                }

                return new SteamGuardDisplayState(code, secondsRemaining, true, null);
            }
            catch (System.Exception)
            {
                return Unavailable(secondsRemaining);
            }
        }

        private static SteamGuardDisplayState Unavailable(int secondsRemaining)
        {
            return new SteamGuardDisplayState("", secondsRemaining, false, "Account does not contain a valid authenticator");
        }
    }
}
