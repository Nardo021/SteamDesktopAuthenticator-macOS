using Avalonia.Controls;
using SDA.Desktop.Views;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IEncryptionPrompt
    {
        Task<string> PromptAsync(string errorMessage);
    }

    public sealed class SilentEncryptionPrompt : IEncryptionPrompt
    {
        public static readonly SilentEncryptionPrompt Instance = new SilentEncryptionPrompt();

        public Task<string> PromptAsync(string errorMessage)
        {
            return Task.FromResult<string>(null);
        }
    }

    public sealed class EncryptionPrompt : IEncryptionPrompt
    {
        private readonly Window _owner;
        private readonly ApplicationLifecycleService _lifecycle;

        public EncryptionPrompt(Window owner)
            : this(owner, null)
        {
        }

        public EncryptionPrompt(Window owner, ApplicationLifecycleService lifecycle)
        {
            _owner = owner;
            _lifecycle = lifecycle;
        }

        public async Task<string> PromptAsync(string errorMessage)
        {
            EncryptionPasswordWindow window = new EncryptionPasswordWindow();
            window.SetError(errorMessage);
            if (_lifecycle != null)
            {
                _lifecycle.BeginUnlockPrompt();
            }

            try
            {
                if (_owner != null)
                {
                    _owner.Activate();
                }

                bool unlocked = await window.ShowDialog<bool>(_owner);
                if (!unlocked)
                {
                    return null;
                }

                return window.Password;
            }
            finally
            {
                if (_lifecycle != null)
                {
                    _lifecycle.EndUnlockPrompt();
                }
            }
        }
    }
}
