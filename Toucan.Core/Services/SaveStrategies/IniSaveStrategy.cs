using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class IniSaveStrategy : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Adb;

    private readonly IFileService _fileService;

    public IniSaveStrategy(IFileService fileService)
    {
        _fileService = fileService;
    }

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var kv in context.LanguageDictionary)
        {
            var language = kv.Key;
            var list = kv.Value;
            var sb = new StringBuilder();

            // INI file header comment
            sb.AppendLine($"; Translation file for {language}");
            sb.AppendLine();

            foreach (var item in list.NoEmpty())
            {
                // Convert namespace dots to sections if hierarchical
                // For simplicity, we'll use a flat structure with keys
                var key = item.Namespace.Replace('.', '_');
                var value = EscapeIniValue(item.Value ?? string.Empty);
                sb.AppendLine($"{key}={value}");
            }

            _fileService.SaveText(path, language + ".ini", sb.ToString());
        }
    }

    private static string EscapeIniValue(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Basic INI escaping - wrap in quotes if contains special chars
        if (input.Contains(';') || input.Contains('#') || input.Contains('=') || 
            input.StartsWith(' ') || input.EndsWith(' '))
        {
            return $"\"{input.Replace("\"", "\\\"")}\"";
        }

        return input;
    }

    public async Task SaveAsync(string path, SaveContext context)
    {
        await Task.Run(() => Save(path, context));
    }
}
