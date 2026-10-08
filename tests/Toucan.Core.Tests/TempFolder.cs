namespace Toucan.Core.Tests;

internal static class TempFolder
{
    /// <summary>
    /// Removes a test folder, ignoring files that cannot be deleted. Windows keeps a loaded plugin assembly locked
    /// for the life of the process (plugin load contexts are not collectible), and a leftover temp folder is no reason to fail a test.
    /// </summary>
    public static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
