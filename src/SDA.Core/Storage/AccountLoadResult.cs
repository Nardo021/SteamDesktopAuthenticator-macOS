using SteamAuth;

namespace SDA.Core.Storage
{
    public enum AccountLoadStatus
    {
        Success,
        NoAccounts,
        PasswordRequired,
        InvalidPassword
    }

    public sealed class AccountLoadResult
    {
        private AccountLoadResult(AccountLoadStatus status, SteamGuardAccount[] accounts)
        {
            Status = status;
            Accounts = accounts ?? new SteamGuardAccount[0];
        }

        public AccountLoadStatus Status { get; }

        public SteamGuardAccount[] Accounts { get; }

        public static AccountLoadResult Success(SteamGuardAccount[] accounts)
        {
            return new AccountLoadResult(AccountLoadStatus.Success, accounts);
        }

        public static AccountLoadResult NoAccounts()
        {
            return new AccountLoadResult(AccountLoadStatus.NoAccounts, new SteamGuardAccount[0]);
        }

        public static AccountLoadResult PasswordRequired()
        {
            return new AccountLoadResult(AccountLoadStatus.PasswordRequired, new SteamGuardAccount[0]);
        }

        public static AccountLoadResult InvalidPassword()
        {
            return new AccountLoadResult(AccountLoadStatus.InvalidPassword, new SteamGuardAccount[0]);
        }
    }
}
