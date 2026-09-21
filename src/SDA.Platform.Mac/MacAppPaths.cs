using System;
using System.IO;

namespace SDA.Platform.Mac
{
    public static class MacAppPaths
    {
        public const string ApplicationFolderName = "Steam Desktop Authenticator";
        public const string MaFilesFolderName = "maFiles";
        public const string SettingsFileName = "settings.json";

        public static string GetApplicationSupportDirectory()
        {
            string applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(applicationData))
            {
                throw new InvalidOperationException("The user application data directory is unavailable.");
            }

            return Path.Combine(applicationData, ApplicationFolderName);
        }

        public static string GetDefaultMaFilesDirectory()
        {
            return Path.Combine(GetApplicationSupportDirectory(), MaFilesFolderName);
        }

        public static string GetSettingsFilePath()
        {
            return Path.Combine(GetApplicationSupportDirectory(), SettingsFileName);
        }
    }
}
