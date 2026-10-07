using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads CSV translation files. Expected format: key,lang1,lang2,... or key,language,value.</summary>
public class CsvLoadStrategy : ILoadStrategy
{
    public string FormatId => FormatIds.Csv;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = FileEnumerator.EnumerateFiles(folder, "*.csv")
            .Concat(FileEnumerator.EnumerateFiles(folder, "*.tsv"));

        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            var sepChar = file.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            var records = ParseRecords(File.ReadAllText(file), sepChar);
            if (records.Count < 2) continue;

            var header = records[0];

            if (header.Length >= 3 && header[1].Equals("language", StringComparison.OrdinalIgnoreCase))
            {
                // Format: key,language,value
                for (int i = 1; i < records.Count; i++)
                {
                    var cols = records[i];
                    if (cols.Length < 3) continue;
                    items.Add(new TranslationItem { Language = cols[1], Namespace = cols[0], Value = cols[2] });
                }
            }
            else
            {
                // Format: key,en,fr,de,... (columns = languages)
                var languages = header[1..];
                for (int i = 1; i < records.Count; i++)
                {
                    var cols = records[i];
                    if (cols.Length < 2) continue;
                    var key = cols[0];
                    for (int j = 0; j < languages.Length && j + 1 < cols.Length; j++)
                        items.Add(new TranslationItem { Language = languages[j], Namespace = key, Value = cols[j + 1] });
                }
            }
        }
        return items;
    }

    /// <summary>RFC 4180 parser: quoted fields may contain separators, quotes and line breaks.</summary>
    internal static List<string[]> ParseRecords(string text, char sep)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false, any = false;

        void EndRecord()
        {
            fields.Add(current.ToString());
            current.Clear();
            // A line holding a single empty field is a blank line, not a record.
            if (fields.Count > 1 || fields[0].Length > 0 || any) records.Add(fields.ToArray());
            fields.Clear();
            any = false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                any = true;
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"') { current.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (inQuotes) current.Append(c);
            else if (c == sep) { fields.Add(current.ToString()); current.Clear(); }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                EndRecord();
            }
            else current.Append(c);
        }
        if (current.Length > 0 || fields.Count > 0 || any) EndRecord();
        return records;
    }
}
