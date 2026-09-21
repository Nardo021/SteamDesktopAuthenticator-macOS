using SDA.Desktop.Models;
using System;
using System.IO;
using System.Text.Json;

namespace SDA.Desktop.Services
{
    public sealed class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly string _settingsPath;

        public SettingsService(string settingsPath)
        {
            if (string.IsNullOrEmpty(settingsPath))
            {
                throw new ArgumentException("Settings path is empty", nameof(settingsPath));
            }

            _settingsPath = settingsPath;
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new AppSettings();
                }

                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions);
                return settings ?? new AppSettings();
            }
            catch (Exception)
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            string directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
    }
}
