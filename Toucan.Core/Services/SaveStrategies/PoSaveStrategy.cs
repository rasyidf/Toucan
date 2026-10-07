using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class PoSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Po;
    public string DisplayName => "Gettext PO";
    public IReadOnlyList<string> FileExtensions => [".po"];
    public string DefaultFilePath(string language) => $"{language}.po";

    public bool StoresCommentsInline => true;
    public FormatDetection Detection { get; } = new(2, [".po", ".pot"], []);

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        var root = Path.GetFullPath(path);
        foreach (var (language, list) in context.LanguageDictionary)
        {
            // Entries go back to the file they were loaded from; new ones go to <language>.po.
            var byFile = list.NoEmpty().GroupBy(i => FileFor(i, language, root));
            var written = false;
            foreach (var group in byFile)
            {
                written = true;
                WriteFile(root, group.Key, language, group.ToList());
            }
            if (!written) WriteFile(root, DefaultFilePath(language), language, []);
        }
    }

    private static string FileFor(TranslationItem item, string language, string root)
    {
        if (item.FormatData?.TryGetValue(PoFormat.File, out var rel) == true && !string.IsNullOrEmpty(rel))
        {
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return rel;
        }
        return $"{language}.po";
    }

    private void WriteFile(string root, string relative, string language, List<TranslationItem> items)
    {
        var sb = new StringBuilder();

        var header = items.Select(i => i.FormatData?.GetValueOrDefault(PoFormat.Header)).FirstOrDefault(h => h != null)
                     ?? $"Language: {language}\n";
        var headerComment = items.Select(i => i.FormatData?.GetValueOrDefault(PoFormat.HeaderComment)).FirstOrDefault(h => h != null);
        foreach (var line in Lines(headerComment)) sb.AppendLine(line.Length == 0 ? "#" : "# " + line);
        sb.AppendLine("msgid \"\"");
        AppendField(sb, "msgstr", header);
        sb.AppendLine();

        foreach (var item in items)
        {
            var data = item.FormatData;
            foreach (var line in Lines(item.Comment)) sb.AppendLine(line.Length == 0 ? "#" : "# " + line);
            foreach (var line in Lines(data?.GetValueOrDefault(PoFormat.Extracted))) sb.AppendLine("#. " + line);
            foreach (var line in Lines(data?.GetValueOrDefault(PoFormat.Refs))) sb.AppendLine("#: " + line);

            var flags = new List<string>();
            if (data != null)
            {
                if (data.TryGetValue(PoFormat.Fuzzy, out _) && !item.IsApproved) flags.Add("fuzzy");
                flags.AddRange((data.GetValueOrDefault(PoFormat.Flags) ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries));
            }
            if (flags.Count > 0) sb.AppendLine("#, " + string.Join(", ", flags));

            var key = item.Namespace;
            var sep = key.IndexOf(PoFormat.ContextSeparator);
            if (sep >= 0)
            {
                AppendField(sb, "msgctxt", key[..sep]);
                key = key[(sep + 1)..];
            }
            AppendField(sb, "msgid", key);

            if (data?.TryGetValue(PoFormat.IdPlural, out var plural) == true)
            {
                AppendField(sb, "msgid_plural", plural ?? "");
                AppendField(sb, "msgstr[0]", item.Value ?? "");
                var forms = data.Where(kv => kv.Key.StartsWith(PoFormat.PluralPrefix))
                    .Select(kv => (N: int.Parse(kv.Key[PoFormat.PluralPrefix.Length..]), kv.Value))
                    .OrderBy(f => f.N);
                foreach (var (n, text) in forms) AppendField(sb, $"msgstr[{n}]", text);
            }
            else
            {
                AppendField(sb, "msgstr", item.Value ?? string.Empty);
            }
            sb.AppendLine();
        }

        var full = Path.Combine(root, relative);
        fileService.SaveText(Path.GetDirectoryName(full)!, Path.GetFileName(full), sb.ToString());
    }

    private static IEnumerable<string> Lines(string? s) =>
        string.IsNullOrEmpty(s) ? [] : s.Split('\n');

    /// <summary>Writes <c>name "value"</c>, wrapped gettext-style (empty first line) when the value has line breaks.</summary>
    private static void AppendField(StringBuilder sb, string name, string value)
    {
        var parts = value.Split('\n');
        if (parts.Length == 1 || (parts.Length == 2 && parts[1].Length == 0))
        {
            sb.AppendLine($"{name} \"{Escape(value)}\"");
            return;
        }

        sb.AppendLine($"{name} \"\"");
        for (int i = 0; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            if (last && parts[i].Length == 0) break;
            sb.AppendLine($"\"{Escape(parts[i])}{(last ? "" : "\\n")}\"");
        }
    }

    private static string Escape(string input) =>
        string.IsNullOrEmpty(input) ? string.Empty :
        input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\t", "\\t").Replace("\n", "\\n");

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
