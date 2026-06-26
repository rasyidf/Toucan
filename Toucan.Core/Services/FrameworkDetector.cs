using System.IO;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Detects i18n framework from folder structure to auto-select SaveStyle.
/// ponytail: simple file-extension heuristic, no deep parsing.
/// </summary>
public static class FrameworkDetector
{
    public static SaveStyles Detect(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return SaveStyles.Json;

        if (Directory.GetFiles(folder, "*.arb", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.Arb;
        if (Directory.GetFiles(folder, "*.resx", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.Resx;
        if (Directory.GetFiles(folder, "*.po", SearchOption.AllDirectories).Length > 0
            || Directory.GetFiles(folder, "*.pot", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.Properties;
        if (Directory.GetFiles(folder, "*.xlf", SearchOption.AllDirectories).Length > 0
            || Directory.GetFiles(folder, "*.xliff", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.Xliff;
        if (Directory.GetFiles(folder, "*.strings", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.IosStrings;
        if (Directory.GetFiles(folder, "strings.xml", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.AndroidXml;
        if (Directory.GetFiles(folder, "*.properties", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.JavaProperties;
        if (Directory.GetFiles(folder, "*.php", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.LaravelPhp;
        if (Directory.GetFiles(folder, "*.yaml", SearchOption.AllDirectories).Length > 0
            || Directory.GetFiles(folder, "*.yml", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.Yaml;
        if (Directory.GetFiles(folder, "*.toml", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.Toml;
        if (Directory.GetFiles(folder, "*.ini", SearchOption.AllDirectories).Length > 0)
            return SaveStyles.Adb;
        if (Directory.Exists(Path.Combine(folder, "locales")))
            return SaveStyles.Namespaced;

        return SaveStyles.Json;
    }
}
