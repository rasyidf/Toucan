using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class CsvSaveStrategy : ISaveStrategy
{
    public string FormatId => FormatIds.Csv;

    public FormatSupport Support { get; } = new(
        FormatEditing.Full,
        "RFC 4180 CSV: key column plus one column per language",
        ["Quoted fields", "embedded commas", "quotes and line breaks"],
        ["Only the key and language columns are kept; extra columns are dropped"]);
    public string DisplayName => "CSV";
    public IReadOnlyList<string> FileExtensions => [".csv"];
    public string DefaultFilePath(string language) => "translations.csv";

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null || context.Languages == null) return;

        var languages = context.Languages;
        var allItems = context.LanguageDictionary.SelectMany(kv => kv.Value).ToList();
        var namespaces = allItems.ToNamespaces().OrderBy(n => n).ToList();

        // Rows end with the file's line ending (LineEndings); a line break inside a quoted value is written as the value
        // holds it, as spreadsheets do, so the whole text is not converted.
        var file = Path.Combine(path, "translations.csv");
        var newline = LineEndings.For(file);
        var sb = new StringBuilder();
        // Header: key,lang1,lang2,...
        sb.Append("key," + string.Join(',', languages)).Append(newline);

        foreach (var ns in namespaces)
        {
            sb.Append(CsvEscape(ns));
            foreach (var lang in languages)
            {
                var value = allItems.FirstOrDefault(i => i.Namespace == ns && i.Language == lang)?.Value ?? "";
                sb.Append(',' + CsvEscape(value));
            }
            sb.Append(newline);
        }

        Directory.CreateDirectory(path);
        AtomicFile.WriteAllText(file, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvEscape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
