using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class TomlSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Toml;

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

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
