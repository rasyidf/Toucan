using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class JavaPropertiesSaveStrategy(IFileService fileService) : ISaveStrategy, IResourceSaveStrategy, IResourceOptionsProvider
{
    public IReadOnlySet<string> SupportedOptions(ResourceDescriptor resource) => ResourceOptionFields.With("UnicodeEscapes");

    public string FormatId => FormatIds.JavaProperties;

    public FormatSupport Support { get; } = new(
        FormatEditing.Full,
        "Java .properties (UTF-8 or ISO-8859-1 with \\uXXXX escapes); line continuations",
        ["Escapes", "unicode", "leading spaces", "line continuations", "multiline text", "exact-resource comments/order/original escapes", "selected output encoding and Unicode escape policy"],
        ["Folder-only saves sort keys and omit source comments; exact resources preserve document presentation"]);
    public string DisplayName => "Java .properties";
    public IReadOnlyList<string> FileExtensions => [".properties"];
    public string DefaultFilePath(string language) => $"{language}.properties";

    public IReadOnlyList<string> LanguageFiles(string projectRoot, string language)
    {
        var files = Directory.Exists(projectRoot) ? Directory.EnumerateFiles(projectRoot, "*.properties", SearchOption.AllDirectories)
            .Where(f => Path.GetFileNameWithoutExtension(f) == language || Path.GetFileNameWithoutExtension(f).EndsWith("_" + language, StringComparison.Ordinal)).ToList() : [];
        return files.Count > 0 ? files : [Path.Combine(projectRoot, DefaultFilePath(language))];
    }

    public FormatDetection Detection { get; } = new(6, [".properties"], []);

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var entries = list.ToList();
            var existingFiles = entries.Select(i => i.FormatData?.GetValueOrDefault("properties.file")).Where(f => f != null).Distinct(StringComparer.Ordinal).ToList();
            var defaultFile = existingFiles.Count == 1 ? existingFiles[0]! : language + ".properties";
            foreach (var group in entries.GroupBy(i => i.FormatData?.GetValueOrDefault("properties.file") ?? defaultFile))
            {
                var utf8 = group.Any(i => i.FormatData?.GetValueOrDefault("properties.encoding") == "UTF-8");
                var sb = new StringBuilder();
                sb.AppendLine("# Translation file for " + language);
                foreach (var item in group.ForSave(context).OrderBy(i => i.Namespace))
                {
                    var key = EscapeKey(item.Namespace);
                    var value = EscapeValue(item.Value ?? string.Empty, utf8);
                    sb.AppendLine($"{key}={value}");
                }
                var target = Path.GetFullPath(Path.Combine(path, group.Key));
                if (!target.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Properties output must stay inside the project.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Toucan.Core.Services.AtomicFile.WriteAllText(target, sb.ToString(), utf8 ? new UTF8Encoding(false) : Encoding.Latin1);
            }
        }
    }

    public ResourceWriteResult WriteResource(ResourceWriteRequest request)
    {
        var entries = ResourceFileAdapter.Entries(request);
        var file = ResourceFilePath.Resolve(request.ProjectRoot, request.Document.Resource.RelativePath);
        var options = ResourceText.Options(request);
        var original = request.Document.Data.TryGetValue("properties", out var saved) ? saved.GetString() ?? "" : "";
        var content = PropertiesDocumentCodec.Write(original, entries, options);
        ResourceText.Write(request, content, latin: true);
        request.Document.Data["properties"] = System.Text.Json.JsonSerializer.SerializeToElement(content);
        return new([]);
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));

    private static string EscapeKey(string s) => s.Replace("\\", "\\\\").Replace(" ", "\\ ").Replace("=", "\\=").Replace(":", "\\:").Replace("#", "\\#").Replace("!", "\\!").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private static string EscapeValue(string s, bool utf8)
    {
        var sb = new StringBuilder(s.Length);
        var leading = true;
        foreach (var c in s)
        {
            // Readers skip whitespace after the separator, so leading spaces must be escaped to survive.
            if (c == ' ' && leading) { sb.Append("\\ "); continue; }
            leading = false;
            if (c > '~' && !utf8) sb.Append($"\\u{(int)c:X4}");
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
