using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class JavaPropertiesSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.JavaProperties;

    public FormatSupport Support { get; } = new(
        FormatEditing.Full,
        "Java .properties (ISO-8859-1 with \\uXXXX escapes); line continuations",
        ["Escapes", "unicode", "leading spaces", "line continuations", "multiline text"],
        ["Comments in the file are not kept", "Key order is sorted on save"]);
    public string DisplayName => "Java .properties";
    public IReadOnlyList<string> FileExtensions => [".properties"];
    public string DefaultFilePath(string language) => $"{language}.properties";

    public FormatDetection Detection { get; } = new(6, [".properties"], []);

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Translation file for " + language);
            foreach (var item in list.NoEmpty().OrderBy(i => i.Namespace))
            {
                var key = EscapeKey(item.Namespace);
                var value = EscapeValue(item.Value ?? string.Empty);
                sb.AppendLine($"{key}={value}");
            }
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, language + ".properties"), sb.ToString(), Encoding.Latin1);
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));

    private static string EscapeKey(string s) => s.Replace(" ", "\\ ").Replace("=", "\\=").Replace(":", "\\:");

    private static string EscapeValue(string s)
    {
        var sb = new StringBuilder(s.Length);
        var leading = true;
        foreach (var c in s)
        {
            // Readers skip whitespace after the separator, so leading spaces must be escaped to survive.
            if (c == ' ' && leading) { sb.Append("\\ "); continue; }
            leading = false;
            if (c > '~') sb.Append($"\\u{(int)c:X4}");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\t') sb.Append("\\t");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\f') sb.Append("\\f");
            else if (c == '\\') sb.Append("\\\\");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
