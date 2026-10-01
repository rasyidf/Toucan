using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Sample.Plugin;

/// <summary>
/// One file per language, named <c>{language}.tsv</c>, one <c>key&lt;TAB&gt;value</c> line per translation.
/// Backslash, tab and line breaks inside values are escaped as <c>\\</c>, <c>\t</c>, <c>\n</c>, <c>\r</c>.
/// </summary>
public sealed class TsvFormat : ISaveStrategy, ILoadStrategy
{
    // Stable, lowercase, unique across Toucan and every plugin. Stored in each project's toucan.tproj as "saveFormat".
    public string FormatId => "sample-tsv";

    // Shown in the export picker and `toucan list-formats`.
    public string DisplayName => "Tab-separated values (sample)";

    // Lets "import by file extension" pick this format.
    public IReadOnlyList<string> FileExtensions => [".tsv"];

    // Where new projects put a language's file, relative to the project folder, always with '/' separators.
    public string DefaultFilePath(string language) => $"{language}.tsv";

    // Optional: lets Toucan recognise folders of .tsv files when there is no project file yet.
    // Lower priority numbers win; built-ins use 0-10.
    public FormatDetection Detection { get; } = new(Priority: 12, Extensions: [".tsv"], FileNames: []);

    public void Save(string path, SaveContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Directory.CreateDirectory(path);

        foreach (var (language, items) in context.LanguageDictionary)
        {
            var lines = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Namespace))
                .OrderBy(i => i.Namespace, StringComparer.Ordinal)
                .Select(i => $"{Escape(i.Namespace)}\t{Escape(i.Value)}");
            File.WriteAllLines(Path.Combine(path, DefaultFilePath(language)), lines, new UTF8Encoding(false));
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var items = new List<TranslationItem>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.tsv").Order(StringComparer.OrdinalIgnoreCase))
        {
            var language = Path.GetFileNameWithoutExtension(file);
            foreach (var line in File.ReadLines(file, Encoding.UTF8))
            {
                var tab = line.IndexOf('\t', StringComparison.Ordinal);
                if (tab <= 0) continue; // blank line, or no key

                items.Add(new TranslationItem
                {
                    Language = language,
                    Namespace = Unescape(line[..tab]),
                    Value = Unescape(line[(tab + 1)..]),
                });
            }
        }

        return items;
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal);

    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal)) return value;

        var result = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i == value.Length - 1)
            {
                result.Append(value[i]);
                continue;
            }

            i++;
            result.Append(value[i] switch { 't' => '\t', 'n' => '\n', 'r' => '\r', var other => other });
        }

        return result.ToString();
    }
}
