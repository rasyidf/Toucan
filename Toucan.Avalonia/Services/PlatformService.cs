using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input.Platform;

namespace Toucan.Avalonia.Services;

/// <summary>
/// OS integration that differs between Windows, macOS, and Linux: opening URLs,
/// revealing folders in the file manager, launching an external editor, and the clipboard.
/// </summary>
internal static class PlatformService
{
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>Human-readable name of the platform file manager, for menu labels.</summary>
    public static string FileManagerName => IsMacOS ? "Finder" : IsWindows ? "Explorer" : "File Manager";

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            if (IsWindows)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (IsMacOS)
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing to fall back to; the caller shows no error for a failed browser launch.
        }
    }

    /// <summary>Opens <paramref name="path"/> in the platform file manager.</summary>
    public static void RevealInFileManager(string path)
    {
        if (string.IsNullOrEmpty(path) || !(Directory.Exists(path) || File.Exists(path))) return;
        try
        {
            if (IsWindows)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else if (IsMacOS)
                Process.Start("open", File.Exists(path) ? ["-R", path] : [path]);
            else
                Process.Start("xdg-open", Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Runs an external editor command line such as <c>code --goto "{file}:{line}"</c>
    /// through the platform shell so PATH lookups and quoting behave as in a terminal.
    /// </summary>
    public static void RunShellCommand(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return;
        try
        {
            var psi = IsWindows
                ? new ProcessStartInfo("cmd", ["/c", commandLine])
                : new ProcessStartInfo("/bin/sh", ["-lc", commandLine]);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private static IClipboard? Clipboard => AppWindows.Active?.Clipboard ?? AppWindows.Main?.Clipboard;

    public static async Task SetClipboardTextAsync(string? text)
    {
        if (string.IsNullOrEmpty(text) || Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(text);
    }

    public static async Task<string?> GetClipboardTextAsync()
    {
        return Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
    }
}
