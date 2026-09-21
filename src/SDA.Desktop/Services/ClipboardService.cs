using Avalonia.Controls;
using Avalonia.Input.Platform;
using System;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IClipboardService
    {
        Task SetTextAsync(string text);
    }

    public sealed class ClipboardService : IClipboardService
    {
        private readonly Func<TopLevel> _topLevel;

        public ClipboardService(Func<TopLevel> topLevel)
        {
            _topLevel = topLevel;
        }

        public async Task SetTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            TopLevel topLevel = _topLevel == null ? null : _topLevel();
            IClipboard clipboard = topLevel == null ? null : topLevel.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            await clipboard.SetTextAsync(text);
        }
    }
}
