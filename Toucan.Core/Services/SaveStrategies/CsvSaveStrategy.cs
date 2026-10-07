using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class CsvSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Csv;
    public string DisplayName => "CSV";
    public IReadOnlyList<string> FileExtensions => [".csv"];
    public string DefaultFilePath(string language) => "translations.csv";

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null || context.Languages == null) return;

        var languages = context.Languages;
        var allItems = context.LanguageDictionary.SelectMany(kv => kv.Value).ToList();
        var namespaces = allItems.ToNamespaces().OrderBy(n => n).ToList();

        var sb = new StringBuilder();
        // Header: key,lang1,lang2,...
        sb.AppendLine("key," + string.Join(',', languages));

        foreach (var ns in namespaces)
        {
            sb.Append(CsvEscape(ns));
            foreach (var lang in languages)
            {
                var value = allItems.FirstOrDefault(i => i.Namespace == ns && i.Language == lang)?.Value ?? "";
                sb.Append(',' + CsvEscape(value));
            }
            sb.AppendLine();
        }

        fileService.SaveText(path, "translations.csv", sb.ToString());
    }

    private static string CsvEscape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? $"\"{s.Replace("\"", "\"\"")}\""
            : s;

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
