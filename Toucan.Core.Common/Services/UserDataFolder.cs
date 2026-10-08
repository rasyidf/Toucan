using System.IO;

namespace Toucan.Core.Services;

/// <summary>
/// The folder that holds Toucan's per-user files (<c>Toucan/...</c> under it). Normally the user's Documents folder;
/// on systems that report none (a Linux session without a home directory, some CI runners) it falls back to the
/// application-data folders and finally the temp folder, so a relative path is never used.
/// </summary>
public static class UserDataFolder
{
    public static string Root { get; } = Resolve();

    private static string Resolve()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path)) return path;
        }
        return Path.GetTempPath();
    }
}
