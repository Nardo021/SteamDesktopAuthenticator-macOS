using SteamAuth;

namespace SDA.Desktop.ViewModels
{
    public sealed class AccountViewModel
    {
        public AccountViewModel(SteamGuardAccount account)
        {
            Account = account;
        }

        public SteamGuardAccount Account { get; }

        public string DisplayName
        {
            get
            {
                if (Account != null && !string.IsNullOrEmpty(Account.AccountName))
                {
                    return Account.AccountName;
                }

                if (Account != null && Account.Session != null && Account.Session.SteamID != 0)
                {
                    return Account.Session.SteamID.ToString();
                }

                return "Unknown account";
            }
        }

        public ulong SteamId
        {
            get
            {
                if (Account == null || Account.Session == null)
                {
                    return 0;
                }

                return Account.Session.SteamID;
            }
        }
    }
}
