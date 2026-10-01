using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Detects the translation file format of a folder from the <see cref="FormatDetection"/> rules that
/// save strategies declare, so plugin formats take part in detection.
/// </summary>
public class FormatDetector(IEnumerable<ISaveStrategy> saveStrategies)
{
    /// <summary>
    /// Single cancellable walk through <see cref="FileEnumerator"/> (which skips node_modules, .git, etc.)
    /// instead of one full-tree scan per format. Returns a format ID.
    /// </summary>
    public string Detect(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return FormatIds.Json;

        var rules = saveStrategies
            .Select(s => (s.FormatId, Rule: s.Detection))
            .Where(r => r.Rule is not null)
            .OrderBy(r => r.Rule!.Priority)
            .ToList();
        if (rules.Count == 0) return Fallback(folder);

        var found = new bool[rules.Count];
        foreach (var file in FileEnumerator.EnumerateFiles(folder, "*"))
        {
            var name = Path.GetFileName(file);
            var ext = Path.GetExtension(file);
            for (int i = 0; i < rules.Count; i++)
            {
                if (found[i]) continue;
                var rule = rules[i].Rule!;
                if (rule.Extensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase))
                    || rule.FileNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    found[i] = true;
                    if (i == 0) return rules[0].FormatId; // highest priority, nothing can beat it
                }
            }
        }

        for (int i = 0; i < rules.Count; i++)
            if (found[i]) return rules[i].FormatId;

        return Fallback(folder);
    }

    private static string Fallback(string folder) =>
        Directory.Exists(Path.Combine(folder, "locales")) ? FormatIds.Namespaced : FormatIds.Json;
}
