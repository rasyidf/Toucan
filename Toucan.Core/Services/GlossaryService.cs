using System.Globalization;
using System.Text;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public static class GlossaryService
{
    public static bool ContainsTerm(string text, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        var start = 0;
        while ((start = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var end = start + term.Length;
            if ((start == 0 || !WordCharacter(text[start - 1]) || !WordCharacter(term[0])) &&
                (end == text.Length || !WordCharacter(text[end]) || !WordCharacter(term[^1]))) return true;
            start++;
        }
        return false;
    }

    private static bool WordCharacter(char c) => char.IsLetterOrDigit(c) && c is not (>= '\u2e80' and <= '\u9fff');

    public static IReadOnlyList<GlossaryEntry> Matches(IEnumerable<GlossaryEntry> entries, string source) =>
        entries.Where(e => ContainsTerm(source, e.Term)).ToList();

    public static Dictionary<string, Dictionary<string, string>> ForAi(IEnumerable<GlossaryEntry> entries) =>
        entries.Where(e => !e.Forbidden).GroupBy(e => e.Term, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new Dictionary<string, string>(g.Last().Translations));

    public static string Prompt(IEnumerable<GlossaryEntry> entries) => string.Join("\n", entries.Select(e =>
        e.Forbidden ? $"Forbidden term: {e.Term}. {e.Description}" : $"{e.Term}: {string.Join(", ", e.Translations.Select(t => $"{t.Key}={t.Value}"))}. {e.Description}"));

    public static string ExportCsv(IEnumerable<GlossaryEntry> entries)
    {
        static string Cell(string s) => "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var rows = new StringBuilder("term,description,forbidden,language,translation\r\n");
        foreach (var entry in entries)
        {
            var translations = entry.Translations.Count == 0 ? new Dictionary<string, string> { [""] = "" } : entry.Translations;
            foreach (var pair in translations)
                rows.Append(string.Join(',', new[] { entry.Term, entry.Description, entry.Forbidden.ToString(CultureInfo.InvariantCulture), pair.Key, pair.Value }.Select(Cell))).Append("\r\n");
        }
        return rows.ToString();
    }

    public static List<GlossaryEntry> ImportCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>(); var cell = new StringBuilder(); var quoted = false; var closedQuote = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (closedQuote && c is not (',' or '\r' or '\n')) throw new FormatException("Unexpected content after quoted CSV field.");
            if (c == '"')
            {
                if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                else if (quoted || cell.Length == 0) { closedQuote = quoted; quoted = !quoted; }
                else throw new FormatException("Unexpected quote in glossary CSV.");
            }
            else if (!quoted && (c == ',' || c is '\r' or '\n'))
            {
                row.Add(cell.ToString()); cell.Clear(); closedQuote = false;
                if (c != ',') { rows.Add(row); row = []; if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; }
            }
            else cell.Append(c);
        }
        if (quoted) throw new FormatException("Unclosed quote in glossary CSV.");
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        if (rows.Count == 0 || !rows[0].SequenceEqual(new[] { "term", "description", "forbidden", "language", "translation" }))
            throw new FormatException("Expected CSV columns: term,description,forbidden,language,translation.");
        var entries = new Dictionary<string, GlossaryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var values in rows.Skip(1).Where(r => r.Count != 1 || r[0].Length > 0))
        {
            if (values.Count != 5 || string.IsNullOrWhiteSpace(values[0]) || !bool.TryParse(values[2], out var forbidden))
                throw new FormatException("Invalid glossary CSV row.");
            if (!entries.TryGetValue(values[0], out var entry)) entries[values[0]] = entry = new() { Term = values[0], Description = values[1], Forbidden = forbidden };
            if (entry.Description != values[1] || entry.Forbidden != forbidden) throw new FormatException("Conflicting glossary term definitions.");
            if (values[3].Length > 0 && !entry.Translations.TryAdd(values[3], values[4])) throw new FormatException("Duplicate term/language in glossary CSV.");
        }
        return entries.Values.ToList();
    }
}
