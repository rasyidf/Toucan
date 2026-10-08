using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class IniSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.Ini;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "INI sections",
        ["Export only: flat key=value lines, values with special characters quoted"],
        ["Cannot be opened again by Toucan (no loader)", "Dots in keys are written as underscores", "Sections and comments are not written"]);
    public string DisplayName => "INI";
    public IReadOnlyList<string> FileExtensions => [".ini"];
    public string DefaultFilePath(string language) => $"{language}.ini";

    public FormatDetection Detection { get; } = new(10, [".ini"], []);

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var sb = new StringBuilder($"; Translation file for {language}\n\n");
            foreach (var item in list.NoEmpty())
            {
                var key = item.Namespace.Replace('.', '_');
                var value = item.Value ?? string.Empty;
                // Quote if contains special chars
                if (value.Contains(';') || value.Contains('#') || value.Contains('=') || value.StartsWith(' ') || value.EndsWith(' '))
                    value = $"\"{value.Replace("\"", "\\\"")}\"";
                sb.AppendLine($"{key}={value}");
            }
            fileService.SaveText(path, language + ".ini", sb.ToString());
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
