using System.IO;

namespace SDA.Platform.Mac
{
    public static class MacPlatformServices
    {
        public static void EnsureDefaultStorage()
        {
            EnsureDirectories(MacAppPaths.GetApplicationSupportDirectory(), MacAppPaths.GetDefaultMaFilesDirectory());
        }

        public static void EnsureDirectories(string applicationSupportDirectory, string maFilesDirectory)
        {
            Directory.CreateDirectory(applicationSupportDirectory);
            Directory.CreateDirectory(maFilesDirectory);
        }
    }
}
