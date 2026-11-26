using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// MIG-01. Toucan 0.23 saved each language of a YAML or <c>.properties</c> project to a second file at the project root, named as
/// the format's default (<c>en.yaml</c> beside <c>config/locales/en.yml</c>), and wrote later edits only there, sometimes twice per
/// key. Such a project shows every key twice and has not opened since 0.25. This merges each copy back into its language's file:
/// keys are matched by language and key, a value in the copy that differs from the original wins (0.23 never wrote the originals,
/// so a differing copy value is an edit made in Toucan), keys only in the copy are added to the original, and the copies are moved
/// to a backup on the next save.
/// </summary>
public static class LegacyCopyMerge
{
    /// <returns>Null when the project has no such copies, or when a language has more than one original file to merge into.</returns>
    public static LegacyCopyMergeResult? Apply(ISaveStrategy writer, IReadOnlyList<TranslationItem> items)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(items);
        static string? FileKey(TranslationItem i) => i.FormatData?.Keys.FirstOrDefault(k => k.EndsWith(".file", StringComparison.Ordinal));
        static string? FileOf(TranslationItem i) => FileKey(i) is { } key ? i.FormatData![key].Replace('\\', '/').TrimStart('/') : null;

        var drop = new HashSet<TranslationItem>(ReferenceEqualityComparer.Instance);
        var copies = new List<string>();
        var changes = new List<string>();
        foreach (var language in items.GroupBy(i => i.Language, StringComparer.Ordinal))
        {
            var copyPath = writer.DefaultFilePath(language.Key).Replace('\\', '/');
            var copy = language.Where(i => FileOf(i) == copyPath).ToList();
            var originals = language.Where(i => FileOf(i) is { } f && f != copyPath).ToList();
            if (copy.Count == 0 || originals.Count == 0) continue;
            var files = originals.Select(FileOf).Distinct(StringComparer.Ordinal).ToList();
            if (files.Count != 1) return null;
            var original = files[0]!;
            var byKey = originals.GroupBy(i => i.Namespace, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            foreach (var key in copy.GroupBy(i => i.Namespace, StringComparer.Ordinal))
            {
                if (byKey.TryGetValue(key.Key, out var target))
                {
                    var edited = key.Select(i => i.Value ?? string.Empty).Where(v => v != (target.Value ?? string.Empty)).Distinct(StringComparer.Ordinal).ToList();
                    if (edited.Count > 0)
                    {
                        changes.Add($"{language.Key} {key.Key}: kept \"{edited[0]}\" from {copyPath} over \"{target.Value}\" in {original}"
                            + (edited.Count > 1 ? $" (the copy also had \"{string.Join("\", \"", edited.Skip(1))}\")" : "") + ".");
                        target.Value = edited[0];
                    }
                    if (string.IsNullOrEmpty(target.Comment) && key.FirstOrDefault(i => !string.IsNullOrEmpty(i.Comment)) is { } commented) target.Comment = commented.Comment;
                    target.IsApproved |= key.Any(i => i.IsApproved);
                    foreach (var item in key) drop.Add(item);
                }
                else
                {
                    var kept = key.First();
                    kept.FormatData![FileKey(kept)!] = original;
                    changes.Add($"{language.Key} {key.Key}: added to {original} from {copyPath}.");
                    foreach (var item in key.Skip(1)) drop.Add(item);
                }
            }
            copies.Add(copyPath);
        }
        return copies.Count == 0 ? null : new([.. items.Where(i => !drop.Contains(i))], copies, changes);
    }

    /// <summary>Moves the merged copies to <c>.toucan/backup/0.23-copies/&lt;time&gt;/</c>, after the save that replaced them.</summary>
    public static string? MoveCopiesToBackup(string projectRoot, IReadOnlyCollection<string> copies, DateTimeOffset now)
    {
        if (copies.Count == 0) return null;
        var backup = Path.Combine(projectRoot, ".toucan", "backup", "0.23-copies", now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var copy in copies)
        {
            var source = Path.Combine(projectRoot, copy);
            if (!File.Exists(source)) continue;
            var target = Path.Combine(backup, copy);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target);
        }
        return backup;
    }
}

/// <summary>Entries after merging 0.23 copies, the copies merged, and one line per value that differed or was added.</summary>
public sealed record LegacyCopyMergeResult(List<TranslationItem> Items, IReadOnlyList<string> Copies, IReadOnlyList<string> Changes);
