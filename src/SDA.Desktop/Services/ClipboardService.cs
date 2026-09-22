using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using System;
using System.Diagnostics;
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

            IClipboard clipboard = ResolveClipboard();
            if (clipboard != null)
            {
                try
                {
                    await clipboard.SetTextAsync(text);
                    return;
                }
                catch (Exception)
                {
                }
            }

            WriteMacPasteboard(text);
        }

        private IClipboard ResolveClipboard()
        {
            TopLevel topLevel = _topLevel == null ? null : _topLevel();
            if (topLevel != null && topLevel.Clipboard != null)
            {
                return topLevel.Clipboard;
            }

            IClassicDesktopStyleApplicationLifetime desktop = Application.Current == null
                ? null
                : Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop == null)
            {
                return null;
            }

            if (desktop.MainWindow != null && desktop.MainWindow.Clipboard != null)
            {
                return desktop.MainWindow.Clipboard;
            }

            if (desktop.Windows != null)
            {
                foreach (Window window in desktop.Windows)
                {
                    if (window != null && window.Clipboard != null)
                    {
                        return window.Clipboard;
                    }
                }
            }

            return null;
        }

        private static void WriteMacPasteboard(string text)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(start))
            {
                if (process == null)
                {
                    return;
                }

                process.StandardInput.Write(text);
                process.StandardInput.Close();
                process.WaitForExit(1000);
            }
        }
    }
}
