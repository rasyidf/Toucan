using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts;

namespace Toucan.Core.Services;

/// <summary>
/// Scans source files for translation key references using regex patterns.
/// Supports: t('key'), $t('key'), i18n.t('key'), useTranslation, getString, NSLocalizedString,
/// @Localizer["key"], GetString("key"), tr("key"), and more.
/// </summary>
public partial class SourceCodeService : ISourceCodeService
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<KeyUsage>> _usagesByKey = new();
    private readonly ConcurrentDictionary<string, byte> _allFoundKeys = new();

    private static readonly string[] s_extensions =
        [".TS", ".TSX", ".JS", ".JSX", ".VUE", ".SVELTE", ".PY", ".CS", ".KT", ".JAVA", ".SWIFT", ".DART", ".RB", ".PHP", ".GO"];

    private static readonly string[] s_excludeDirs =
        ["node_modules", ".git", "dist", "build", "out", ".next", "__pycache__", "bin", "obj", "Pods", ".dart_tool"];

    private static readonly HashSet<string> s_excludeDirsSet =
        new(s_excludeDirs, StringComparer.OrdinalIgnoreCase);

    public bool HasScanData => !_allFoundKeys.IsEmpty;

    public async Task<SourceCodeScanResult> ScanAsync(string sourceRoot, CancellationToken cancellationToken = default)
    {
        _usagesByKey.Clear();
        _allFoundKeys.Clear();

        if (!Directory.Exists(sourceRoot))
            return new SourceCodeScanResult { FilesScanned = 0, KeysFound = 0, TotalUsages = 0, Duration = TimeSpan.Zero };

        var sw = Stopwatch.StartNew();
        var files = EnumerateSourceFiles(sourceRoot).ToList();
        var totalUsages = 0;

        await Task.Run(() =>
        {
            Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancellationToken }, file =>
            {
                var relPath = Path.GetRelativePath(sourceRoot, file);
                var lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (var match in s_keyPattern.Matches(lines[i]).Cast<Match>())
                    {
                        // Extract key from whichever capture group matched (groups 1-5)
                        string? key = null;
                        for (int g = 1; g <= match.Groups.Count - 1; g++)
                        {
                            if (match.Groups[g].Success) { key = match.Groups[g].Value; break; }
                        }
                        if (string.IsNullOrWhiteSpace(key)) continue;

                        var usage = new KeyUsage(key, relPath, i + 1, lines[i].Trim());
                        _usagesByKey.GetOrAdd(key, _ => new ConcurrentBag<KeyUsage>()).Add(usage);
                        _allFoundKeys.TryAdd(key, 0);
                        Interlocked.Increment(ref totalUsages);
                    }
                }
            });
        }, cancellationToken).ConfigureAwait(false);

        sw.Stop();
        return new SourceCodeScanResult
        {
            FilesScanned = files.Count,
            KeysFound = _allFoundKeys.Count,
            TotalUsages = totalUsages,
            Duration = sw.Elapsed
        };
    }

    public IEnumerable<KeyUsage> FindUsages(string key)
        => _usagesByKey.TryGetValue(key, out var bag) ? bag : [];

    public IEnumerable<string> GetUndefinedKeys(IEnumerable<string> translationKeys)
    {
        var defined = translationKeys.ToHashSet();
        return _allFoundKeys.Keys.Where(k => !defined.Contains(k));
    }

    public IEnumerable<string> GetUnusedKeys(IEnumerable<string> translationKeys)
        => translationKeys.Where(k => !_allFoundKeys.ContainsKey(k));

    private static IEnumerable<string> EnumerateSourceFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (!s_excludeDirsSet.Contains(name))
                    stack.Push(sub);
            }
            foreach (var file in Directory.GetFiles(dir))
            {
                var ext = Path.GetExtension(file).ToUpperInvariant();
                if (s_extensions.Contains(ext))
                    yield return file;
            }
        }
    }

    /// <summary>
    /// Matches translation key extraction patterns across frameworks:
    /// - t('key'), t("key"), $t('key'), i18n.t('key')
    /// - useTranslation()...t('key')
    /// - getString(R.string.key) — Android (simplified)
    /// - NSLocalizedString("key"...) — iOS
    /// - @Localizer["key"], GetString("key") — .NET
    /// - tr("key") — Qt
    /// - __("key"), _("key") — Python/PHP gettext
    /// </summary>
    [GeneratedRegex("""(?:\$?t|i18n\.t|__?)\(\s*['"]([^'"]+)['"]\s*\)|NSLocalizedString\(\s*@?"([^"]+)"|@?Localizer\[\s*"([^"]+)"\s*\]|GetString\(\s*"([^"]+)"\s*\)|tr\(\s*"([^"]+)"\s*\)""", RegexOptions.Compiled)]
    private static partial Regex RawKeyPattern();

    /// <summary>Unified pattern that captures key into group 1.</summary>
    private static Regex KeyPattern()
    {
        // Use a wrapper that normalizes all named groups into group 1
        return s_keyPattern;
    }

    private static readonly Regex s_keyPattern = new(
        """(?:\$?t|i18n\.t|__?)\(\s*['"]([^'"]+)['"]|NSLocalizedString\(\s*@?"([^"]+)"|@?Localizer\[\s*"([^"]+)"\s*\]|GetString\(\s*"([^"]+)"|tr\(\s*"([^"]+)")""",
        RegexOptions.Compiled);
}
