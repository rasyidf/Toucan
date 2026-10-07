using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class TomlSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Toml;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "TOML basic and literal strings; [section] and dotted keys; one file per language",
        ["Sections", "dotted keys", "multiline text via escapes", "unicode escapes"],
        ["Comments in the file are not kept", "Arrays, inline tables, multiline (\"\"\") strings and dates are not read"]);
    public string DisplayName => "TOML";
    public IReadOnlyList<string> FileExtensions => [".toml"];
    public string DefaultFilePath(string language) => $"{language}.toml";

    public FormatDetection Detection { get; } = new(9, [".toml"], []);

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var sb = new StringBuilder($"# Translation file for {language}\n\n");

            // Group by first namespace segment as TOML sections
            var grouped = list.NoEmpty()
                .GroupBy(t => t.Namespace.Contains('.') ? t.Namespace[..t.Namespace.IndexOf('.')] : "")
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                if (!string.IsNullOrEmpty(group.Key))
                    sb.AppendLine($"\n[{group.Key}]");

                foreach (var item in group.OrderBy(i => i.Namespace))
                {
                    var key = string.IsNullOrEmpty(group.Key) ? item.Namespace : item.Namespace[(group.Key.Length + 1)..];
                    sb.AppendLine($"{key} = \"{Escape(item.Value ?? "")}\"");
                }
            }

            fileService.SaveText(path, language + ".toml", sb.ToString());
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
