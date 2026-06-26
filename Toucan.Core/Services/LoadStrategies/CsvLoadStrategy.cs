using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads CSV translation files. Expected format: key,lang1,lang2,... or key,language,value.</summary>
public class CsvLoadStrategy : ILoadStrategy
{
    public SaveStyles Style => SaveStyles.Csv;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.csv", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folder, "*.tsv", SearchOption.AllDirectories));

        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            if (lines.Length < 2) continue;

            var sep = file.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            var header = SplitCsv(lines[0], sep);

            if (header.Length >= 3 && header[1].Equals("language", StringComparison.OrdinalIgnoreCase))
            {
                // Format: key,language,value
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = SplitCsv(lines[i], sep);
                    if (cols.Length < 3) continue;
                    items.Add(new TranslationItem { Language = cols[1], Namespace = cols[0], Value = cols[2] });
                }
            }
            else
            {
                // Format: key,en,fr,de,... (columns = languages)
                var languages = header[1..];
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = SplitCsv(lines[i], sep);
                    if (cols.Length < 2) continue;
                    var key = cols[0];
                    for (int j = 0; j < languages.Length && j + 1 < cols.Length; j++)
                        items.Add(new TranslationItem { Language = languages[j], Namespace = key, Value = cols[j + 1] });
                }
            }
        }
        return items;
    }

    private static string[] SplitCsv(string line, char sep)
    {
        // Simple CSV split handling quoted fields
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == sep && !inQuotes) { fields.Add(current.ToString()); current.Clear(); continue; }
            current.Append(c);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
