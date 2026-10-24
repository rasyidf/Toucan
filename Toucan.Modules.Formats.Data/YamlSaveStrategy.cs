using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class YamlSaveStrategy(IFileService fileService) : ISaveStrategy, IResourceSaveStrategy, IResourceOptionsProvider, IResourceDialectProvider
{
    public IReadOnlyList<ResourceDialectChoice> Dialects => [new(null, "Generic YAML"), new("rails-yaml", "Rails locale wrapper")];
    public IReadOnlySet<string> SupportedOptions(ResourceDescriptor resource) => ResourceOptionFields.With("Layout", "Delimiter", "Arrays", "Indent");

    public string FormatId => FormatIds.Yaml;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "YAML block maps/sequences and JSON-compatible scalar types; restricted dialect",
        ["Nested and flat dotted keys (same style as the file)", "scalar and mapping sequences", "Declared Rails locale roots and original file paths", "exact-resource literal/nested keys, nulls and empty nodes", "block scalars (| and >) read", "multiline text", "quoted scalars such as on", "off", "~ and numbers"],
        ["Comments in the file are not kept", "Anchors, aliases, tags, nonempty flow collections and unsupported syntax block saving", "Block scalars are written as quoted strings"]);
    public string DisplayName => "YAML";
    public IReadOnlyList<string> FileExtensions => [".yml", ".yaml"];
    public string DefaultFilePath(string language) => $"{language}.yaml";

    public IReadOnlyList<string> LanguageFiles(string projectRoot, string language)
    {
        var files = Directory.Exists(projectRoot) ? Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f) is ".yml" or ".yaml" && Path.GetFileNameWithoutExtension(f) == language).ToList() : [];
        return files.Count > 0 ? files : [Path.Combine(projectRoot, DefaultFilePath(language))];
    }

    public FormatDetection Detection { get; } = new(8, [".yaml", ".yml"], []);

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var kv in context.LanguageDictionary)
        {
            var language = kv.Key;
            var entries = kv.Value.ToList();
            var existingFiles = entries.Select(i => i.FormatData?.GetValueOrDefault("yaml.file")).Where(f => f != null).Distinct(StringComparer.Ordinal).ToList();
            var defaultFile = existingFiles.Count == 1 ? existingFiles[0]! : language + ".yaml";
            foreach (var group in entries.GroupBy(i => i.FormatData?.GetValueOrDefault("yaml.file") ?? defaultFile))
            {
                var list = group.ToList();
                var locale = list.Select(i => i.FormatData?.GetValueOrDefault("yaml.locale")).FirstOrDefault(v => v != null);
                var sb = new StringBuilder();

                // YAML header comment
                sb.AppendLine($"# Translation file for {language}");
                sb.AppendLine("---");

                // Build a nested structure from the namespaces
                var dict = new Dictionary<string, string>();
                foreach (var item in list.ForSave(context))
                {
                    dict[locale == null ? item.Namespace : locale + "." + item.Namespace] = item.Value ?? string.Empty;
                }

                // Keep the style the file already uses; a key that is also a parent can only be written flat.
                if (UseFlatKeys(path, language, dict))
                    WriteFlat(sb, dict);
                else
                    WriteYamlDict(sb, dict, 0);

                var target = Path.GetFullPath(Path.Combine(path, group.Key));
                if (!target.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("YAML output must stay inside the project.");
                fileService.SaveText(Path.GetDirectoryName(target)!, Path.GetFileName(target), sb.ToString());
            }
        }
    }

    public IReadOnlyList<string> FindUnsupportedConstructs(string projectRoot)
    {
        var found = new List<string>();
        foreach (var file in FileEnumerator.EnumerateFiles(projectRoot, "*.yml").Concat(FileEnumerator.EnumerateFiles(projectRoot, "*.yaml")))
            try { YamlDocumentCodec.Read(File.ReadAllText(file)); }
            catch (FormatException ex) { found.Add($"{Path.GetRelativePath(projectRoot, file)}: {ex.Message}"); }
        return found;
    }

    public ResourceWriteResult WriteResource(ResourceWriteRequest request)
    {
        ResourceFileAdapter.Entries(request, allowStructuredKeys: true); // Validate identities without flattening their typed paths.
        var file = ResourceFilePath.Resolve(request.ProjectRoot, request.Document.Resource.RelativePath);
        if (request.Document.Data.TryGetValue("yaml.unsafe", out var unsafeInput) && unsafeInput.ValueKind == System.Text.Json.JsonValueKind.String) throw new FormatException($"{request.Document.Resource.RelativePath}: save blocked: {unsafeInput.GetString()}");
        if (File.Exists(file)) YamlDocumentCodec.Read(ResourceText.Read(new(request.ProjectRoot, request.Document.Resource, request.ProjectOptions, request.CancellationToken)));
        var options = ResourceText.Options(request);
        var json = ResourceJsonCodec.Write(request.Document, request.Entries, request.ProjectOptions);
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var locale = ResourceFileAdapter.Metadata(request.Document).GetValueOrDefault("yaml.locale");
        var language = request.Document.Resource.Language;
        if (request.Document.Resource.Dialect == "rails-yaml" && (locale == null || locale != language && locale != language.Split('-')[0])) locale = language;
        var wrapped = locale != null ? new System.Text.Json.Nodes.JsonObject { [locale] = root.DeepClone() } : root;
        var content = YamlDocumentCodec.Write(wrapped, options);
        ResourceText.Write(request, content);
        ResourceJsonCodec.AcceptWrite(request.Document, request.Entries, json);
        request.Document.Data["yaml.source"] = System.Text.Json.JsonSerializer.SerializeToElement(content);
        return new([]);
    }

    private static bool UseFlatKeys(string path, string language, Dictionary<string, string> dict)
    {
        if (HasKeyClash(dict)) return true;
        foreach (var ext in new[] { ".yaml", ".yml" })
        {
            var existing = Path.Combine(path, language + ext);
            if (File.Exists(existing)) return ExistingFileIsFlat(existing);
        }
        return false;
    }

    /// <summary>True when a key is also the parent of another key (<c>app</c> and <c>app.title</c>).</summary>
    private static bool HasKeyClash(Dictionary<string, string> dict) =>
        dict.Keys.Any(k => dict.Keys.Any(o => o.Length > k.Length && o.StartsWith(k + ".", StringComparison.Ordinal)));

    private static bool ExistingFileIsFlat(string file)
    {
        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0 || line[0] == ' ' || line[0] == '\t' || line[0] == '#' || line[0] == '-') continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().Trim('"', '\'');
            if (key.Contains('.')) return true;
        }
        return false;
    }

    private static void WriteFlat(StringBuilder sb, Dictionary<string, string> dict)
    {
        foreach (var kv in dict.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.AppendLine($"{EscapeYamlKey(kv.Key)}: {EscapeYamlValue(kv.Value)}");
    }

    private static string EscapeYamlKey(string key) =>
        key.IndexOfAny([':', '#', '"', '\'', '{', '}', '[', ']', ',', '&', '*', '!', '|', '>', '%', '@', '`']) >= 0
        || key.StartsWith(' ') || key.EndsWith(' ') || key.StartsWith('-') || key.StartsWith('?')
            ? $"\"{key.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
            : key;

    private static void WriteYamlDict(StringBuilder sb, Dictionary<string, string> dict, int indent)
    {
        // Numeric bracket segments are sequence members, matching JSON's internal key convention.
        var arrayRoots = dict.Keys.Where(k => k.Contains('[') && !k[..k.IndexOf('[')].Contains('.'))
            .Select(k => k[..k.IndexOf('[')]).Distinct(StringComparer.Ordinal).ToList();
        foreach (var arrayRoot in arrayRoots)
        {
            sb.AppendLine($"{new string(' ', indent)}{EscapeYamlKey(arrayRoot)}:");
            var members = dict.Where(p => p.Key.StartsWith(arrayRoot + "[", StringComparison.Ordinal))
                .GroupBy(p => int.Parse(p.Key.AsSpan(arrayRoot.Length + 1, p.Key.IndexOf(']') - arrayRoot.Length - 1), System.Globalization.CultureInfo.InvariantCulture))
                .OrderBy(g => g.Key);
            foreach (var member in members)
            {
                var prefix = arrayRoot + "[" + member.Key + "]";
                var scalar = member.FirstOrDefault(p => p.Key == prefix);
                if (scalar.Key != null) sb.AppendLine($"{new string(' ', indent + 2)}- {EscapeYamlValue(scalar.Value)}");
                else
                {
                    sb.AppendLine($"{new string(' ', indent + 2)}-");
                    WriteYamlDict(sb, member.ToDictionary(p => p.Key[(prefix.Length + 1)..], p => p.Value), indent + 4);
                }
            }
        }
        var grouped = dict
            .Where(p => !arrayRoots.Any(a => p.Key.StartsWith(a + "[", StringComparison.Ordinal)))
            .GroupBy(kvp => GetRootKey(kvp.Key))
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var rootKey = group.Key;
            var items = group.ToList();

            // Check if all items are leaf nodes (no further nesting)
            var isLeaf = items.All(kvp => kvp.Key == rootKey);

            if (isLeaf && items.Count == 1)
            {
                // Simple key-value pair
                var value = EscapeYamlValue(items[0].Value);
                sb.AppendLine($"{new string(' ', indent)}{EscapeYamlKey(rootKey)}: {value}");
            }
            else
            {
                // Nested structure; clashes between a value and a parent never reach here (see HasKeyClash)
                sb.AppendLine($"{new string(' ', indent)}{EscapeYamlKey(rootKey)}:");

                var nested = new Dictionary<string, string>();
                foreach (var item in items)
                {
                    var remainingKey = GetRemainingKey(item.Key, rootKey);
                    if (!string.IsNullOrEmpty(remainingKey))
                    {
                        nested[remainingKey] = item.Value;
                    }
                }

                if (nested.Count > 0)
                {
                    WriteYamlDict(sb, nested, indent + 2);
                }
            }
        }
    }

    private static string GetRootKey(string key)
    {
        var dotIndex = key.IndexOf('.');
        return dotIndex > 0 ? key.Substring(0, dotIndex) : key;
    }

    private static string GetRemainingKey(string key, string rootKey)
    {
        if (key == rootKey)
            return string.Empty;
        
        if (key.StartsWith(rootKey + "."))
            return key.Substring(rootKey.Length + 1);
        
        return key;
    }

    private static string EscapeYamlValue(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "\"\"";

        // Values that YAML would interpret as special types need quoting
        bool needsQuote = input.Contains(':') || input.Contains('#') || input.Contains('{') || 
            input.Contains('[') || input.Contains('|') || input.Contains('>') ||
            input.Contains('\n') || input.Contains('\r') || input.Contains('\t') ||
            input.Contains('\'') || input.Contains('"') ||
            input.StartsWith(' ') || input.EndsWith(' ') ||
            input.StartsWith('*') || input.StartsWith('&') || input.StartsWith('!') ||
            input.StartsWith('@') || input.StartsWith('`') ||
            string.Equals(input, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "y", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input, "n", StringComparison.OrdinalIgnoreCase) ||
            input == "~" || input == "-" || input.StartsWith("- ") || input.StartsWith('?') ||
            input.StartsWith('%') || input.StartsWith(',') ||
            double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) ||
            (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || input.StartsWith("0o", StringComparison.OrdinalIgnoreCase));

        if (needsQuote)
        {
            // Escape backslashes, quotes, and control characters
            var escaped = input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
            return $"\"{escaped}\"";
        }

        return input;
    }

    public async Task SaveAsync(string path, SaveContext context)
    {
        await Task.Run(() => Save(path, context)).ConfigureAwait(false);
    }
}
