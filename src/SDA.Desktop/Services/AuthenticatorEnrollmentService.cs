using SteamAuth;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IAuthenticatorLinker
    {
        Task<AuthenticatorLinker.LinkResult> AddAuthenticatorAsync();

        Task<AuthenticatorLinker.FinalizeResult> FinalizeAddAuthenticatorAsync(string smsCode);

        string PhoneNumber { get; set; }

        string PhoneCountryCode { get; set; }

        string ConfirmationEmailAddress { get; }

        SteamGuardAccount LinkedAccount { get; }
    }

    public interface IAuthenticatorLinkerFactory
    {
        IAuthenticatorLinker Create(SessionData session);
    }

    public sealed class SteamAuthAuthenticatorLinker : IAuthenticatorLinker
    {
        private readonly AuthenticatorLinker _linker;

        public SteamAuthAuthenticatorLinker(SessionData session)
        {
            _linker = new AuthenticatorLinker(session);
        }

        public Task<AuthenticatorLinker.LinkResult> AddAuthenticatorAsync()
        {
            return _linker.AddAuthenticator();
        }

        public Task<AuthenticatorLinker.FinalizeResult> FinalizeAddAuthenticatorAsync(string smsCode)
        {
            return _linker.FinalizeAddAuthenticator(smsCode);
        }

        public string PhoneNumber
        {
            get { return _linker.PhoneNumber; }
            set { _linker.PhoneNumber = value; }
        }

        public string PhoneCountryCode
        {
            get { return _linker.PhoneCountryCode; }
            set { _linker.PhoneCountryCode = value; }
        }

        public string ConfirmationEmailAddress
        {
            get { return _linker.ConfirmationEmailAddress; }
        }

        public SteamGuardAccount LinkedAccount
        {
            get { return _linker.LinkedAccount; }
        }
    }

    public sealed class SteamAuthAuthenticatorLinkerFactory : IAuthenticatorLinkerFactory
    {
        public IAuthenticatorLinker Create(SessionData session)
        {
            return new SteamAuthAuthenticatorLinker(session);
        }
    }

    public sealed class PhoneValidationResult
    {
        public PhoneValidationResult(bool valid, string phoneNumber, string countryCode, string error)
        {
            Valid = valid;
            PhoneNumber = phoneNumber ?? "";
            CountryCode = countryCode ?? "";
            Error = error ?? "";
        }

        public bool Valid { get; }

        public string PhoneNumber { get; }

        public string CountryCode { get; }

        public string Error { get; }
    }

    public static class AuthenticatorEnrollmentService
    {
        public const string AlreadyLinkedMessage = LoginChallengeHandler.ExistingAuthenticatorRequiredMessage;
        public const string EmailConfirmationMessage = "Please check your email and click the link Steam sent you before continuing.";
        public const string PhoneAddFailedMessage = "Failed to add your phone number. Please try again or use a different phone number.";
        public const string AuthenticatorPresentMessage = "This account already has an authenticator linked. You must remove that authenticator before adding SDA.";
        public const string GeneralLinkFailureMessage = "Error adding your authenticator.";
        public const string InitialSaveFailedMessage = "Unable to save mobile authenticator file. The authenticator has not been finalized.";
        public const string RevocationIncorrectMessage = "Revocation code incorrect. The authenticator has not been finalized.";
        public const string UnableToGenerateCodesMessage = "Steam Guard could not generate the expected codes and finalization could not be completed.";
        public const string FinalizeFailedMessage = "Unable to finalize this authenticator.";
        public const string FinalSaveFailedMessage = "The authenticator was finalized with Steam, but SDA could not save the finalized account state.";
        public const string SuccessMessage = "Mobile authenticator successfully linked. Please keep your revocation code safe:";

        private static readonly Regex DisallowedPhoneCharacters = new Regex(@"[^0-9\s\+]", RegexOptions.CultureInvariant);
        private static readonly Regex DisallowedCountryCharacters = new Regex(@"[^a-zA-Z]", RegexOptions.CultureInvariant);

        public static PhoneValidationResult ValidatePhone(string phoneNumber, string countryCode)
        {
            string filtered = FilterPhoneNumber(phoneNumber);
            string country = countryCode == null ? "" : countryCode.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(filtered) || filtered[0] != '+')
            {
                return new PhoneValidationResult(false, filtered, country, "Phone number must start with + and country code.");
            }

            if (DisallowedPhoneCharacters.IsMatch(filtered))
            {
                return new PhoneValidationResult(false, filtered, country, "Phone number may only contain +, digits, and spaces.");
            }

            if (DisallowedCountryCharacters.IsMatch(country))
            {
                return new PhoneValidationResult(false, filtered, country, "Country code may only contain letters.");
            }

            return new PhoneValidationResult(true, filtered, country, "");
        }

        public static string FilterPhoneNumber(string phoneNumber)
        {
            if (phoneNumber == null)
            {
                return "";
            }

            return phoneNumber.Replace("-", "").Replace("(", "").Replace(")", "");
        }

        public static bool RevocationMatches(string entered, string expected)
        {
            if (expected == null)
            {
                return false;
            }

            return string.Equals(entered == null ? "" : entered.ToUpper(), expected, StringComparison.Ordinal);
        }

        public static void ApplyPhone(IAuthenticatorLinker linker, string phoneNumber, string countryCode)
        {
            if (linker == null)
            {
                return;
            }

            linker.PhoneNumber = phoneNumber;
            linker.PhoneCountryCode = string.IsNullOrEmpty(countryCode) ? null : countryCode;
        }

        public static void ClearPhone(IAuthenticatorLinker linker)
        {
            if (linker != null)
            {
                linker.PhoneNumber = null;
            }
        }

        public static Task<AuthenticatorLinker.LinkResult> AddAuthenticatorAsync(IAuthenticatorLinker linker, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return linker.AddAuthenticatorAsync();
        }

        public static Task<AuthenticatorLinker.FinalizeResult> FinalizeAsync(IAuthenticatorLinker linker, string smsCode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return linker.FinalizeAddAuthenticatorAsync(smsCode);
        }
    }
}
