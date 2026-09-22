using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IMaFilePicker
    {
        Task<string> PickAsync();
    }

    public sealed class MaFilePicker : IMaFilePicker
    {
        private readonly Func<Window> _window;

        public MaFilePicker(Func<Window> window)
        {
            _window = window;
        }

        public async Task<string> PickAsync()
        {
            Window window = _window == null ? null : _window();
            if (window == null || window.StorageProvider == null)
            {
                return null;
            }

            IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Account",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("maFiles")
                    {
                        Patterns = new[] { "*.maFile" }
                    }
                }
            });

            if (files == null || files.Count == 0)
            {
                return null;
            }

            return files[0].TryGetLocalPath();
        }
    }
}
