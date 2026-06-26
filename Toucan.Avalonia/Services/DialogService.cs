using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Toucan.Avalonia.Services;

public class DialogService : IDialogService
{
    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public async Task<string?> SelectFolderAsync(string? initialPath = null)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storage = window.StorageProvider;
        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(initialPath))
            startFolder = await storage.TryGetFolderFromPathAsync(new Uri("file:///" + initialPath.Replace('\\', '/')));

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder",
            SuggestedStartLocation = startFolder,
            AllowMultiple = false
        });

        return result.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> SelectFileAsync(string? initialPath = null, string filter = "All Files")
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var storage = window.StorageProvider;
        IStorageFolder? startFolder = null;
        if (!string.IsNullOrEmpty(initialPath))
            startFolder = await storage.TryGetFolderFromPathAsync(new Uri("file:///" + initialPath.Replace('\\', '/')));

        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select File",
            SuggestedStartLocation = startFolder,
            AllowMultiple = false
        });

        return result.FirstOrDefault()?.Path.LocalPath;
    }
}
