using System.IO;
using System.Text.RegularExpressions;

namespace Toucan.Core.Services;

/// <summary>
/// Options controlling file enumeration behavior.
/// </summary>
[Flags]
public enum EnumerateOptions
{
    /// <summary>Default: recurse with standard directory exclusions.</summary>
    None = 0,

    /// <summary>
    /// Skip nested i18n locale directories (e.g., "locales/", "i18n/", "translations/")
    /// when the root folder already contains language-code subdirectories.
    /// Prevents loading duplicates from both `en/common.json` and `locales/en/common.json`.
    /// </summary>
    SkipNestedLocaleDirs = 1 << 0,

    /// <summary>Exclude Toucan audit and comment sidecars when discovering translation files.</summary>
    SkipToucanSidecars = 1 << 1,
}

/// <summary>
/// Centralized file enumeration with directory exclusion.
/// All load strategies should use this instead of raw Directory.GetFiles with SearchOption.AllDirectories.
/// </summary>
public static partial class FileEnumerator
{
    private static readonly HashSet<string> s_excludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".toucan", ".git", ".svn", ".hg",
        "node_modules", ".next", ".nuxt",
        "dist", "build", "out", "obj", "bin",
        ".idea", ".vscode", ".vs",
        "vendor", "__pycache__", ".dart_tool",
        "Pods", "target"
    };

    private static readonly HashSet<string> s_localeDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "locales", "locale", "i18n", "translations", "lang", "langs"
    };

    [GeneratedRegex(@"^[a-z]{2}(-[A-Za-z0-9]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageCodePattern();

    /// <summary>
    /// Recursively enumerates files matching a pattern, skipping excluded directories.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string root, string pattern, EnumerateOptions options = EnumerateOptions.None, HashSet<string>? excludedFiles = null)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            yield break;

        bool skipNestedLocales = options.HasFlag(EnumerateOptions.SkipNestedLocaleDirs);
        bool hasLangDirsAtRoot = skipNestedLocales && HasLanguageSubdirectories(root);

        var scan = ScanContext.Current;
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            scan?.OnDirectory(dir);
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0 || s_excludedDirs.Contains(name))
                    continue;
                // Skip nested locale dirs at root level when root already has lang dirs
                if (hasLangDirsAtRoot && dir == root && s_localeDirNames.Contains(name))
                    continue;
                stack.Push(sub);
            }
            foreach (var file in Directory.GetFiles(dir, pattern))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                if (options.HasFlag(EnumerateOptions.SkipToucanSidecars) && IsToucanSidecar(file))
                    continue;
                if (excludedFiles != null && excludedFiles.Contains(Path.GetFileName(file)))
                    continue;
                scan?.OnFile();
                yield return file;
            }
        }
    }

    /// <summary>
    /// Recursively enumerates files matching multiple patterns, skipping excluded directories.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string root, string[] patterns, EnumerateOptions options = EnumerateOptions.None, HashSet<string>? excludedFiles = null)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            yield break;

        bool skipNestedLocales = options.HasFlag(EnumerateOptions.SkipNestedLocaleDirs);
        bool hasLangDirsAtRoot = skipNestedLocales && HasLanguageSubdirectories(root);

        var scan = ScanContext.Current;
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
            scan?.OnDirectory(dir);
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0 || s_excludedDirs.Contains(name))
                    continue;
                if (hasLangDirsAtRoot && dir == root && s_localeDirNames.Contains(name))
                    continue;
                stack.Push(sub);
            }
            foreach (var p in patterns)
            {
                foreach (var file in Directory.GetFiles(dir, p))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                    if (options.HasFlag(EnumerateOptions.SkipToucanSidecars) && IsToucanSidecar(file))
                        continue;
                    if (excludedFiles != null && excludedFiles.Contains(Path.GetFileName(file)))
                        continue;
                    scan?.OnFile();
                    yield return file;
                }
            }
        }
    }

    /// <summary>Check if a directory name should be excluded from scanning.</summary>
    public static bool IsExcludedDirectory(string dirName) => s_excludedDirs.Contains(dirName);

    private static bool IsToucanSidecar(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals(".toucan-metadata.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".comments.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Checks if the given folder contains subdirectories that look like language codes (e.g., en, id, fr-FR).</summary>
    public static bool HasLanguageSubdirectories(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return false;
        return Directory.GetDirectories(folder)
            .Any(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0 && LanguageCodePattern().IsMatch(Path.GetFileName(d)));
    }
}
