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

    /// <summary>
    /// How much of the title bar the system window controls cover: only macOS has any (the traffic lights, on the left).
    /// Windows and Linux use Toucan's own minimize, maximize and close buttons inside the top bar. Nothing is covered in fullscreen.
    /// </summary>
    public static (double Leading, double Trailing) TitleBarInsets(bool macOS, bool fullScreen) =>
        macOS && !fullScreen ? (78, 0) : (0, 0);

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

    /// <summary>Substitute source locations after parsing the trusted editor template; filenames remain arguments.</summary>
    internal static ProcessStartInfo EditorStartInfo(string template, string file, int line, int column)
    {
        var tokens = new List<string>(); var token = new System.Text.StringBuilder(); char quote = '\0'; var started = false;
        for (var at = 0; at < template.Length; at++)
        {
            var ch = template[at];
            if (ch == '\\' && at + 1 < template.Length && template[at + 1] == '"') { token.Append('"'); at++; started = true; continue; }
            if (ch is '\'' or '"')
            {
                if (quote == '\0') { quote = ch; started = true; continue; }
                if (quote == ch) { quote = '\0'; continue; }
            }
            if (char.IsWhiteSpace(ch) && quote == '\0') { if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; } }
            else { token.Append(ch); started = true; }
        }
        if (quote != '\0') throw new FormatException("The external editor template contains an unmatched quote.");
        if (started) tokens.Add(token.ToString());
        if (tokens.Count == 0 || tokens[0].Contains('{')) throw new FormatException("Choose an editor executable and location arguments.");
        var executable = tokens[0];
        if (IsWindows && executable.Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                var code = Path.GetFullPath(Path.Combine(directory.Trim('"'), "..", "Code.exe"));
                if (File.Exists(code)) { executable = code; break; }
            }
        }
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in tokens.Skip(1)) info.ArgumentList.Add(argument.Replace("{file}", file, StringComparison.Ordinal)
            .Replace("{line}", Math.Max(1, line).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{column}", Math.Max(1, column).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        return info;
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
