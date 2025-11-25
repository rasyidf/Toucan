using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class PoSaveStrategy : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Properties;

    private readonly IFileService _fileService;

    public PoSaveStrategy(IFileService fileService)
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

            // PO file header
            sb.AppendLine("# Translation file");
            sb.AppendLine($"# Language: {language}");
            sb.AppendLine("msgid \"\"");
            sb.AppendLine("msgstr \"\"");
            sb.AppendLine($"\"Language: {language}\\n\"");
            sb.AppendLine();

            foreach (var item in list.NoEmpty())
            {
                // msgctxt is the namespace/key
                sb.AppendLine($"msgctxt \"{EscapePoString(item.Namespace)}\"");
                sb.AppendLine($"msgid \"{EscapePoString(item.Namespace)}\"");
                sb.AppendLine($"msgstr \"{EscapePoString(item.Value ?? string.Empty)}\"");
                sb.AppendLine();
            }

            _fileService.SaveText(path, language + ".po", sb.ToString());
        }
    }

    private static string EscapePoString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    public async Task SaveAsync(string path, SaveContext context)
    {
        await Task.Run(() => Save(path, context));
    }
}
