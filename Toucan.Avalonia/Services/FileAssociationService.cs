using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Toucan.Avalonia.Services;

/// <summary>Outcome of an integration change; <see cref="Error"/> is set when <see cref="Ok"/> is false.</summary>
internal readonly record struct IntegrationResult(bool Ok, string? Error = null)
{
    public static IntegrationResult Success { get; } = new(true);
    public static IntegrationResult Fail(string error) => new(false, error);
}

/// <summary>
/// Registers Toucan as the handler for <c>.tproj</c> project files and, on Windows, adds an
/// "Open with Toucan" entry to the folder context menu. Windows uses per-user registry keys and Linux
/// uses a per-user <c>.desktop</c> file plus a MIME type, so neither needs elevation. macOS cannot register
/// at run time: <c>Toucan.app</c> declares the type in its Info.plist (see packaging/build-macos-app.sh).
/// </summary>
internal static class FileAssociationService
{
    internal const string Extension = ".tproj";
    internal const string Description = "Toucan Translation Project";

    /// <summary>True when this app can register the association itself (Windows and Linux).</summary>
    public static bool IsSupported => PlatformService.IsWindows || OperatingSystem.IsLinux();

    /// <summary>True when the folder context-menu entry can be managed (Windows only).</summary>
    public static bool FolderEntrySupported => PlatformService.IsWindows;

    /// <summary>Why <see cref="IsSupported"/> is false, for the settings page.</summary>
    public static string UnsupportedReason => PlatformService.IsMacOS
        ? "On macOS the .tproj type is declared by Toucan.app itself. To make Toucan the default, select a .tproj file in Finder, choose Get Info, set Open with to Toucan, and click Change All."
        : "This platform is not supported.";

    public static bool IsInstalled()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;
        if (OperatingSystem.IsWindows()) return WindowsIntegration.IsAssociationInstalled(exe);
        if (OperatingSystem.IsLinux()) return LinuxIntegration.IsInstalled(LinuxIntegration.DefaultDataHome(), exe);
        return false;
    }

    public static IntegrationResult Install()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return IntegrationResult.Fail("Cannot determine the path of this application.");
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsIntegration.InstallAssociation(exe);
            if (OperatingSystem.IsLinux()) return LinuxIntegration.Install(LinuxIntegration.DefaultDataHome(), exe, LinuxIntegration.RunTool);
            return IntegrationResult.Fail(UnsupportedReason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return IntegrationResult.Fail(ex.Message);
        }
    }

    public static IntegrationResult Uninstall()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsIntegration.UninstallAssociation();
            if (OperatingSystem.IsLinux()) return LinuxIntegration.Uninstall(LinuxIntegration.DefaultDataHome(), LinuxIntegration.RunTool);
            return IntegrationResult.Fail(UnsupportedReason);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return IntegrationResult.Fail(ex.Message);
        }
    }

    public static bool IsFolderEntryInstalled() =>
        OperatingSystem.IsWindows() && Environment.ProcessPath is { Length: > 0 } exe && WindowsIntegration.IsFolderEntryInstalled(exe);

    public static IntegrationResult InstallFolderEntry()
    {
        if (!OperatingSystem.IsWindows()) return IntegrationResult.Fail("The folder menu entry is only available on Windows.");
        if (string.IsNullOrEmpty(Environment.ProcessPath)) return IntegrationResult.Fail("Cannot determine the path of this application.");
        try
        {
            return WindowsIntegration.InstallFolderEntry(Environment.ProcessPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return IntegrationResult.Fail(ex.Message);
        }
    }

    public static IntegrationResult UninstallFolderEntry()
    {
        if (!OperatingSystem.IsWindows()) return IntegrationResult.Fail("The folder menu entry is only available on Windows.");
        try
        {
            return WindowsIntegration.UninstallFolderEntry();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return IntegrationResult.Fail(ex.Message);
        }
    }
}

[SupportedOSPlatform("windows")]
internal static class WindowsIntegration
{
    private const string ProgId = "Toucan.Project";
    private const string ClassesRoot = @"Software\Classes\";
    private const string FolderKey = @"Software\Classes\Directory\shell\Toucan";

    private static string OpenCommand(string exe) => $"\"{exe}\" \"%1\"";

    public static bool IsAssociationInstalled(string exe)
    {
        using var ext = Registry.CurrentUser.OpenSubKey(ClassesRoot + FileAssociationService.Extension);
        if (ext?.GetValue("")?.ToString() != ProgId) return false;
        using var command = Registry.CurrentUser.OpenSubKey(ClassesRoot + ProgId + @"\shell\open\command");
        return string.Equals(command?.GetValue("")?.ToString(), OpenCommand(exe), StringComparison.OrdinalIgnoreCase);
    }

    public static IntegrationResult InstallAssociation(string exe)
    {
        using (var prog = Registry.CurrentUser.CreateSubKey(ClassesRoot + ProgId))
        {
            prog.SetValue("", FileAssociationService.Description);
            prog.SetValue("FriendlyTypeName", FileAssociationService.Description);
            using var icon = prog.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");
            using var command = prog.CreateSubKey(@"shell\open\command");
            command.SetValue("", OpenCommand(exe));
        }

        using (var ext = Registry.CurrentUser.CreateSubKey(ClassesRoot + FileAssociationService.Extension))
        {
            ext.SetValue("", ProgId);
            ext.SetValue("Content Type", "application/json");
            using var openWith = ext.CreateSubKey("OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        NotifyShell();
        return IntegrationResult.Success;
    }

    public static IntegrationResult UninstallAssociation()
    {
        Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + ProgId, false);
        Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + FileAssociationService.Extension, false);
        NotifyShell();
        return IntegrationResult.Success;
    }

    public static bool IsFolderEntryInstalled(string exe)
    {
        using var command = Registry.CurrentUser.OpenSubKey(FolderKey + @"\command");
        return string.Equals(command?.GetValue("")?.ToString(), OpenCommand(exe), StringComparison.OrdinalIgnoreCase);
    }

    public static IntegrationResult InstallFolderEntry(string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(FolderKey);
        key.SetValue("", "Open with Toucan");
        key.SetValue("Icon", $"\"{exe}\",0");
        using var command = key.CreateSubKey("command");
        command.SetValue("", OpenCommand(exe));
        return IntegrationResult.Success;
    }

    public static IntegrationResult UninstallFolderEntry()
    {
        Registry.CurrentUser.DeleteSubKeyTree(FolderKey, false);
        return IntegrationResult.Success;
    }

    private static void NotifyShell() => SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}

/// <summary>
/// Freedesktop registration: <c>applications/toucan.desktop</c> and <c>mime/packages/toucan-project.xml</c> under the
/// user's XDG data home. The data home and the tool runner are parameters so tests can use a temp folder.
/// </summary>
internal static class LinuxIntegration
{
    internal const string MimeType = "application/x-toucan-project";
    internal const string DesktopFileName = "toucan.desktop";
    private const string MimeFileName = "toucan-project.xml";

    public static string DefaultDataHome()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    internal static string DesktopPath(string dataHome) => Path.Combine(dataHome, "applications", DesktopFileName);
    internal static string MimePath(string dataHome) => Path.Combine(dataHome, "mime", "packages", MimeFileName);

    public static bool IsInstalled(string dataHome, string exe)
    {
        if (!File.Exists(MimePath(dataHome)) || !File.Exists(DesktopPath(dataHome))) return false;
        return File.ReadAllText(DesktopPath(dataHome)).Contains($"Exec={ExecValue(exe)} %f", StringComparison.Ordinal);
    }

    /// <summary>Writes the files, then asks the desktop to refresh its caches. Missing tools are not an error: the files are what matter.</summary>
    public static IntegrationResult Install(string dataHome, string exe, Func<string, string[], bool> runTool)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopPath(dataHome))!);
        Directory.CreateDirectory(Path.GetDirectoryName(MimePath(dataHome))!);
        File.WriteAllText(MimePath(dataHome), MimeXml(), new UTF8Encoding(false));
        File.WriteAllText(DesktopPath(dataHome), DesktopEntry(exe), new UTF8Encoding(false));

        runTool("update-mime-database", [Path.Combine(dataHome, "mime")]);
        runTool("update-desktop-database", [Path.Combine(dataHome, "applications")]);
        runTool("xdg-mime", ["default", DesktopFileName, MimeType]);
        return IntegrationResult.Success;
    }

    public static IntegrationResult Uninstall(string dataHome, Func<string, string[], bool> runTool)
    {
        foreach (var path in new[] { DesktopPath(dataHome), MimePath(dataHome) })
            if (File.Exists(path)) File.Delete(path);

        runTool("update-mime-database", [Path.Combine(dataHome, "mime")]);
        runTool("update-desktop-database", [Path.Combine(dataHome, "applications")]);
        return IntegrationResult.Success;
    }

    internal static string DesktopEntry(string exe) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name=Toucan
        Comment=Translation file editor
        Exec={ExecValue(exe)} %f
        Terminal=false
        Categories=Development;Utility;
        MimeType={MimeType};
        StartupWMClass=Toucan

        """;

    internal static string MimeXml() =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
          <mime-type type="{MimeType}">
            <comment>{FileAssociationService.Description}</comment>
            <glob pattern="*{FileAssociationService.Extension}"/>
          </mime-type>
        </mime-info>

        """;

    /// <summary>Quotes a program path for the Exec key (Desktop Entry spec: double quotes, with <c>\ " ` $</c> backslash-escaped and <c>%</c> doubled).</summary>
    internal static string ExecValue(string exe)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in exe)
        {
            if (c is '\\' or '"' or '`' or '$') sb.Append('\\').Append(c);
            else if (c == '%') sb.Append("%%");
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    /// <summary>Runs a helper tool and reports whether it ran and succeeded. A missing tool just returns false.</summary>
    internal static bool RunTool(string tool, string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var process = Process.Start(info);
            if (process == null) return false;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(10_000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
