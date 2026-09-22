using System;
using System.Collections.Generic;
using System.IO;

namespace SDA.Desktop.Services
{
    public sealed class MaFilesDirectorySnapshot
    {
        private readonly string _directory;
        private readonly Dictionary<string, byte[]> _files;

        private MaFilesDirectorySnapshot(string directory, Dictionary<string, byte[]> files)
        {
            _directory = directory;
            _files = files;
        }

        public static MaFilesDirectorySnapshot Capture(string directory)
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                foreach (string path in Directory.GetFiles(directory))
                {
                    files[Path.GetFileName(path)] = File.ReadAllBytes(path);
                }
            }

            return new MaFilesDirectorySnapshot(directory, files);
        }

        public void Restore()
        {
            if (string.IsNullOrEmpty(_directory) || !Directory.Exists(_directory))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(_directory))
            {
                if (!_files.ContainsKey(Path.GetFileName(path)))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }

            foreach (KeyValuePair<string, byte[]> pair in _files)
            {
                string path = Path.Combine(_directory, pair.Key);
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                }

                File.WriteAllBytes(path, pair.Value);
            }
        }
    }
}
