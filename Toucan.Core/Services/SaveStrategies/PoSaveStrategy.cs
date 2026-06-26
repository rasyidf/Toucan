using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class PoSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Properties;

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Translation file");
            sb.AppendLine($"# Language: {language}");
            sb.AppendLine("msgid \"\"");
            sb.AppendLine("msgstr \"\"");
            sb.AppendLine($"\"Language: {language}\\n\"");
            sb.AppendLine();

            foreach (var item in list.NoEmpty())
            {
                sb.AppendLine($"msgctxt \"{Escape(item.Namespace)}\"");
                sb.AppendLine($"msgid \"{Escape(item.Namespace)}\"");
                sb.AppendLine($"msgstr \"{Escape(item.Value ?? string.Empty)}\"");
                sb.AppendLine();
            }

            fileService.SaveText(path, language + ".po", sb.ToString());
        }
    }

    private static string Escape(string input) =>
        string.IsNullOrEmpty(input) ? string.Empty :
        input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
