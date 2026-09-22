using Avalonia;
using SDA.Core.Common;
using SDA.Desktop.Services;
using System;

namespace SDA.Desktop
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            using (SingleInstanceGuard guard = SingleInstanceGuard.TryAcquire())
            {
                if (guard == null)
                {
                    return;
                }

                AppStartup.Current = AppStartupOptions.FromCommandLine(CommandLineStartup.Parse(args));
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
            }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}
