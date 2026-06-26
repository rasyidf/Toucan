using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class JsonSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Json;

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;
        foreach (var (language, list) in context.LanguageDictionary)
        {
            var root = new Dictionary<string, object>();
            foreach (var item in list.NoEmpty())
            {
                SetNestedValue(root, item.Namespace, item.Value ?? string.Empty);
            }
            fileService.Save(path, language + ".json", root);
        }
    }

    // ponytail: parses dot-separated keys with [N] array indices into nested dicts/lists.
    // Ceiling: O(n*d) where n=items, d=depth; fine for i18n files.
    private static void SetNestedValue(Dictionary<string, object> root, string ns, string value)
    {
        var segments = ParseSegments(ns);
        object current = root;

        for (int i = 0; i < segments.Count - 1; i++)
        {
            var seg = segments[i];
            var next = segments[i + 1];
            object child = next.Index.HasValue ? (object)new List<object>() : new Dictionary<string, object>();

            if (current is Dictionary<string, object> dict)
            {
                if (!dict.TryGetValue(seg.Key, out var existing))
                    dict[seg.Key] = child;
                else
                    child = existing;

                if (seg.Index.HasValue)
                {
                    var list = EnsureList(dict, seg.Key);
                    child = EnsureListIndex(list, seg.Index.Value, next.Index.HasValue ? (object)new List<object>() : new Dictionary<string, object>());
                    current = child;
                    continue;
                }
                current = dict[seg.Key];
            }
            else if (current is List<object> list)
            {
                if (seg.Index.HasValue)
                {
                    current = EnsureListIndex(list, seg.Index.Value, next.Index.HasValue ? (object)new List<object>() : new Dictionary<string, object>());
                }
            }
        }

        // Set the final value
        var last = segments[^1];
        if (current is Dictionary<string, object> parentDict)
        {
            if (last.Index.HasValue)
            {
                var list = EnsureList(parentDict, last.Key);
                EnsureListIndex(list, last.Index.Value, value, overwrite: true);
            }
            else
            {
                parentDict[last.Key] = value;
            }
        }
        else if (current is List<object> parentList && last.Index.HasValue)
        {
            EnsureListIndex(parentList, last.Index.Value, value, overwrite: true);
        }
    }

    private static List<object> EnsureList(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var val) && val is List<object> existing)
            return existing;
        var list = new List<object>();
        dict[key] = list;
        return list;
    }

    private static object EnsureListIndex(List<object> list, int index, object defaultValue, bool overwrite = false)
    {
        while (list.Count <= index) list.Add(null!);
        if (overwrite || list[index] == null)
            list[index] = defaultValue;
        return list[index];
    }

    private static readonly Regex ArrayPattern = new(@"\[(\d+)\]", RegexOptions.Compiled);

    private record struct Segment(string Key, int? Index);

    private static List<Segment> ParseSegments(string ns)
    {
        var result = new List<Segment>();
        var parts = ns.Split('.');
        foreach (var part in parts)
        {
            var match = ArrayPattern.Match(part);
            if (match.Success)
            {
                var key = part[..match.Index];
                if (!string.IsNullOrEmpty(key))
                    result.Add(new Segment(key, null));
                result.Add(new Segment(key, int.Parse(match.Groups[1].Value)));
            }
            else
            {
                result.Add(new Segment(part, null));
            }
        }
        return result;
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
