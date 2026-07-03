using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Stateless helpers for bulk operations on translation items.
/// </summary>
public class BulkOperationService
{
    /// <summary>Removes all items whose Namespace is in <paramref name="namespaces"/> (or is a child of one).</summary>
    public int Delete(List<TranslationItem> all, IEnumerable<string> namespaces)
    {
        var set = namespaces.ToHashSet(StringComparer.Ordinal);
        return all.RemoveAll(t => set.Contains(t.Namespace) || set.Any(ns => t.Namespace.StartsWith(ns + ".", StringComparison.Ordinal)));
    }

    /// <summary>Renames items matching <paramref name="keys"/> so their namespace starts with <paramref name="newPrefix"/>.</summary>
    public int MoveNamespace(List<TranslationItem> all, IEnumerable<string> keys, string newPrefix)
    {
        var set = keys.ToHashSet(StringComparer.Ordinal);
        int count = 0;
        foreach (var item in all)
        {
            if (set.Contains(item.Namespace))
            {
                // Replace the full namespace with newPrefix.leafSegment
                var leaf = item.Namespace.Contains('.')
                    ? item.Namespace[(item.Namespace.LastIndexOf('.') + 1)..]
                    : item.Namespace;
                item.Namespace = string.IsNullOrEmpty(newPrefix) ? leaf : $"{newPrefix}.{leaf}";
                count++;
            }
        }
        return count;
    }

    /// <summary>Copies the value from <paramref name="sourceLang"/> to <paramref name="targetLang"/> for items matching <paramref name="keys"/>.</summary>
    public int CopyToLanguage(List<TranslationItem> all, IEnumerable<string> keys, string sourceLang, string targetLang)
    {
        var set = keys.ToHashSet(StringComparer.Ordinal);
        var sourceMap = all
            .Where(t => set.Contains(t.Namespace) && t.Language == sourceLang)
            .ToDictionary(t => t.Namespace, t => t.Value, StringComparer.Ordinal);

        int count = 0;
        foreach (var item in all.Where(t => set.Contains(t.Namespace) && t.Language == targetLang))
        {
            if (sourceMap.TryGetValue(item.Namespace, out var val) && !string.IsNullOrEmpty(val))
            {
                item.Value = val;
                count++;
            }
        }
        return count;
    }
}
