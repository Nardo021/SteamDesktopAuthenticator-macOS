using SDA.Core.Common;

namespace SDA.Desktop
{
    public sealed class AppStartupOptions
    {
        public static readonly AppStartupOptions Empty = new AppStartupOptions(null, false);

        public AppStartupOptions(string encryptionKey, bool startMinimized)
        {
            EncryptionKey = encryptionKey;
            StartMinimized = startMinimized;
        }

        public string EncryptionKey { get; }

        public bool StartMinimized { get; }

        public bool StartHiddenToTray
        {
            get { return StartMinimized; }
        }

        public static AppStartupOptions FromCommandLine(CommandLineOptions options)
        {
            if (options == null)
            {
                return Empty;
            }

            return new AppStartupOptions(options.EncryptionKey, options.Silent);
        }
    }

    public static class AppStartup
    {
        public static AppStartupOptions Current { get; set; } = AppStartupOptions.Empty;
    }
}
