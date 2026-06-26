using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class LaravelPhpSaveStrategy : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.LaravelPhp;

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var langDir = Path.Combine(path, language);
            Directory.CreateDirectory(langDir);

            // Group by first namespace segment (file prefix)
            var byFile = list.NoEmpty()
                .GroupBy(i => i.Namespace.Split('.')[0])
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (file, items) in byFile)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<?php");
                sb.AppendLine();
                sb.AppendLine("return [");

                // Build nested structure
                WriteItems(sb, items, 1);

                sb.AppendLine("];");

                File.WriteAllText(Path.Combine(langDir, file + ".php"), sb.ToString(), new UTF8Encoding(false));
            }
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));

    private static void WriteItems(StringBuilder sb, List<TranslationItem> items, int rootDepth)
    {
        // ponytail: flat write with nested grouping by dot-separated segments after the file prefix
        var tree = new SortedDictionary<string, object>(); // string value or nested dict

        foreach (var item in items.OrderBy(i => i.Namespace))
        {
            var parts = item.Namespace.Split('.');
            // Skip the first segment (file prefix)
            var keys = parts.Skip(1).ToArray();
            if (keys.Length == 0) continue;

            SetNested(tree, keys, item.Value ?? "");
        }

        WriteDict(sb, tree, rootDepth);
    }

    private static void SetNested(SortedDictionary<string, object> dict, string[] keys, string value)
    {
        var current = dict;
        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (!current.TryGetValue(keys[i], out var child) || child is not SortedDictionary<string, object> childDict)
            {
                childDict = new SortedDictionary<string, object>();
                current[keys[i]] = childDict;
            }
            current = (SortedDictionary<string, object>)current[keys[i]];
        }
        current[keys[^1]] = value;
    }

    private static void WriteDict(StringBuilder sb, SortedDictionary<string, object> dict, int indent)
    {
        var pad = new string(' ', indent * 4);
        foreach (var (key, val) in dict)
        {
            if (val is SortedDictionary<string, object> nested)
            {
                sb.AppendLine($"{pad}'{Escape(key)}' => [");
                WriteDict(sb, nested, indent + 1);
                sb.AppendLine($"{pad}],");
            }
            else
            {
                sb.AppendLine($"{pad}'{Escape(key)}' => '{Escape((string)val)}',");
            }
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
}
