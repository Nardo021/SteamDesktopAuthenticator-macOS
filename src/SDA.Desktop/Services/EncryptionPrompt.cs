using Avalonia.Controls;
using SDA.Desktop.Views;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IEncryptionPrompt
    {
        Task<string> PromptAsync(string errorMessage);
    }

    public sealed class EncryptionPrompt : IEncryptionPrompt
    {
        private readonly Window _owner;

        public EncryptionPrompt(Window owner)
        {
            _owner = owner;
        }

        public async Task<string> PromptAsync(string errorMessage)
        {
            EncryptionPasswordWindow window = new EncryptionPasswordWindow();
            window.SetError(errorMessage);
            _owner.Activate();
            bool unlocked = await window.ShowDialog<bool>(_owner);
            if (!unlocked)
            {
                return null;
            }

            return window.Password;
        }
    }
}
