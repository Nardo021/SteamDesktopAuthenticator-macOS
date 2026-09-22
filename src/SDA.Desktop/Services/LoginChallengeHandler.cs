using SteamKit2.Authentication;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IDeviceCodeProvider
    {
        Task<string> GenerateAsync(CancellationToken cancellationToken);
    }

    public sealed class SteamGuardDeviceCodeProvider : IDeviceCodeProvider
    {
        private readonly SteamAuth.SteamGuardAccount _account;

        public SteamGuardDeviceCodeProvider(SteamAuth.SteamGuardAccount account)
        {
            _account = account;
        }

        public Task<string> GenerateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_account == null)
            {
                return Task.FromResult<string>(null);
            }

            return _account.GenerateSteamGuardCodeAsync();
        }
    }

    public sealed class LoginChallengeHandler : IAuthenticator
    {
        public const string ExistingAuthenticatorRequiredMessage =
            "This account already has an authenticator linked. You must remove that authenticator before adding SDA.";

        private readonly IDeviceCodeProvider _deviceCodes;
        private readonly Func<string, bool, CancellationToken, Task<string>> _emailCode;
        private readonly Action<string> _reportRepeatedDeviceFailure;
        private readonly CancellationToken _cancellationToken;
        private readonly Func<CancellationToken, Task> _waitForNextCode;
        private int _deviceCodesGenerated;

        public LoginChallengeHandler(
            IDeviceCodeProvider deviceCodes,
            Func<string, bool, CancellationToken, Task<string>> emailCode,
            Action<string> reportRepeatedDeviceFailure,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task> waitForNextCode = null)
        {
            _deviceCodes = deviceCodes;
            _emailCode = emailCode;
            _reportRepeatedDeviceFailure = reportRepeatedDeviceFailure;
            _cancellationToken = cancellationToken;
            _waitForNextCode = waitForNextCode ?? (token => Task.Delay(TimeSpan.FromSeconds(30), token));
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(false);
        }

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (previousCodeWasIncorrect)
            {
                if (_deviceCodesGenerated > 2 && _reportRepeatedDeviceFailure != null)
                {
                    _reportRepeatedDeviceFailure("Steam rejected multiple authenticator codes. Check that SDA is still the authenticator for this account.");
                }

                await _waitForNextCode(_cancellationToken);
            }

            _cancellationToken.ThrowIfCancellationRequested();
            if (_deviceCodes == null)
            {
                throw new InvalidOperationException(ExistingAuthenticatorRequiredMessage);
            }

            string deviceCode = await _deviceCodes.GenerateAsync(_cancellationToken);
            _deviceCodesGenerated++;
            if (deviceCode == null)
            {
                throw new InvalidOperationException(ExistingAuthenticatorRequiredMessage);
            }

            if (deviceCode.Length == 0)
            {
                throw new InvalidOperationException("Account does not contain a valid authenticator");
            }

            return deviceCode;
        }

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_emailCode == null)
            {
                throw new OperationCanceledException();
            }

            string code = await _emailCode(email, previousCodeWasIncorrect, _cancellationToken);
            if (string.IsNullOrEmpty(code))
            {
                throw new OperationCanceledException();
            }

            return code;
        }
    }
}
