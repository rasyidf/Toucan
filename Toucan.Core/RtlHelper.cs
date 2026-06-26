using System;
using System.Collections.Generic;

namespace Toucan.Core;

/// <summary>ponytail: RTL detection for font selection in the editor.</summary>
public static class RtlHelper
{
    private static readonly HashSet<string> s_rtlCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "he", "fa", "ur", "ps", "sd", "yi", "ckb", "ug"
    };

    public static bool IsRtl(string languageCode)
    {
        if (string.IsNullOrEmpty(languageCode)) return false;
        var primary = languageCode.Split('-', '_')[0];
        return s_rtlCodes.Contains(primary);
    }

    /// <summary>Returns a font suitable for RTL scripts, falling back to Segoe UI.</summary>
    public static string GetFontFamily(string languageCode) =>
        IsRtl(languageCode) ? "Segoe UI, Arial, Tahoma" : "Segoe UI";
}
