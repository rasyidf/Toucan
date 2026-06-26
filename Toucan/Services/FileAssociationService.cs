using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Toucan.Services;

/// <summary>
/// ponytail: registers .toucan.project file association in Windows registry.
/// Requires elevation for HKLM; falls back to HKCU for per-user registration.
/// </summary>
internal static class FileAssociationService
{
    private const string ProgId = "Toucan.Project";
    private const string Extension = ".project";
    private const string Description = "Toucan Translation Project";

    public static void Register()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId);
            key?.SetValue("", Description);
            key?.SetValue("FriendlyTypeName", Description);

            using var iconKey = key?.CreateSubKey("DefaultIcon");
            iconKey?.SetValue("", $"\"{exePath}\",0");

            using var cmdKey = key?.CreateSubKey(@"shell\open\command");
            cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");

            using var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Extension);
            extKey?.SetValue("", ProgId);

            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* ponytail: best-effort, don't crash on permission issues */ }
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + Extension, false);
            SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
