using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace Toucan.Services;

/// <summary>
/// Registers the .tproj file association in the Windows registry (per-user, HKCU).
/// Provides Install (register), Clear (unregister), and IsInstalled check.
/// ponytail: best-effort, no elevation required — HKCU only.
/// </summary>
internal static class FileAssociationService
{
    private const string ProgId = "Toucan.Project";
    private const string Extension = ".tproj";
    private const string Description = "Toucan Translation Project";
    private const string FileTypeName = "Toucan Project File";

    /// <summary>Registers .tproj file association for the current user.</summary>
    public static void Install()
    {
        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            // Register ProgId with description, icon, and open command
            using var progKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId);
            progKey?.SetValue("", Description);
            progKey?.SetValue("FriendlyTypeName", FileTypeName);

            using var iconKey = progKey?.CreateSubKey("DefaultIcon");
            iconKey?.SetValue("", $"\"{exePath}\",0");

            using var cmdKey = progKey?.CreateSubKey(@"shell\open\command");
            cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");

            // Associate .tproj extension with ProgId
            using var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Extension);
            extKey?.SetValue("", ProgId);
            extKey?.SetValue("Content Type", "application/json");

            // Register in OpenWithProgids for better Explorer integration
            using var openWithKey = extKey?.CreateSubKey("OpenWithProgids");
            openWithKey?.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

            // Notify Explorer shell of the change
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* ponytail: best-effort, don't crash on permission issues */ }
    }

    /// <summary>Removes the .tproj file association for the current user.</summary>
    public static void Clear()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + Extension, false);

            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    /// <summary>Returns true if the .tproj file association is currently registered.</summary>
    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + Extension);
            return key?.GetValue("")?.ToString() == ProgId;
        }
        catch { return false; }
    }

    /// <summary>Backward compat: keep the old Register/Unregister names working.</summary>
    public static void Register() => Install();
    public static void Unregister() => Clear();

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
