using System.IO;

namespace SDA.Desktop.Services
{
    public static class MaFilesDirectoryPolicy
    {
        public static string ResolveStartupDirectory(string savedDirectory, string defaultDirectory)
        {
            if (!string.IsNullOrWhiteSpace(savedDirectory) && Directory.Exists(savedDirectory))
            {
                return savedDirectory;
            }

            return defaultDirectory ?? "";
        }
    }
}
