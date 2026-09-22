using System;
using System.Diagnostics;

namespace SDA.Desktop.Services
{
    public static class ReleasePageOpener
    {
        public static bool TryOpen(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            string trimmed = url.Trim();
            if (!trimmed.StartsWith("https://github.com/Nardo021/SteamDesktopAuthenticator-macOS", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = trimmed,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
