using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SDA.Desktop.Services
{
    public interface IFolderPicker
    {
        Task<string> PickAsync();
    }

    public sealed class FolderPicker : IFolderPicker
    {
        private readonly Func<Window> _window;

        public FolderPicker(Func<Window> window)
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

            IReadOnlyList<IStorageFolder> folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Open maFiles Folder",
                AllowMultiple = false
            });

            if (folders == null || folders.Count == 0)
            {
                return null;
            }

            return folders[0].TryGetLocalPath();
        }
    }
}
