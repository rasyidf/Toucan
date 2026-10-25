using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class LaravelPhpSaveStrategy : ISaveStrategy, IResourceSaveStrategy
{
    public string FormatId => FormatIds.LaravelPhp;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "Laravel lang/<locale>/<file>.php returning an array (short and array() syntax)",
        ["Nested arrays", "single- and double-quoted strings", "multiline text", "escapes", "placeholders (:name)"],
        ["Computed values (function calls, constants, numbers) are not read and are dropped on save", "Comments and PHP code around the array are not kept", "JSON language files (lang/en.json) are a separate format"]);
    public string DisplayName => "Laravel PHP";
    public IReadOnlyList<string> FileExtensions => [".php"];
    public string DefaultFilePath(string language) => $"{language}/messages.php";

    // Laravel writes one file per top-level key under {language}/; messages.php is its conventional default.
    public IReadOnlyList<string> LanguageFiles(string projectRoot, string language)
    {
        var dir = Path.Combine(projectRoot, language);
        // If the directory doesn't exist yet, at least indicate the expected directory.
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.php") : [dir];
    }

    // Directory-based: the sidecar sits at language folder level.
    public string CommentSidecarBase(string language) => language;
    public FormatDetection Detection { get; } = new(7, [".php"], []);

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var langDir = Path.Combine(path, language);
            Directory.CreateDirectory(langDir);

            // Group by first namespace segment (file prefix)
            var byFile = list.ForParse()
                .GroupBy(i => i.Namespace.Split('.')[0])
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (file, items) in byFile)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<?php");
                sb.AppendLine();
                sb.AppendLine("return [");

                // Build nested structure
                WriteItems(sb, items.ForSave(context).ToList(), 1);

                sb.AppendLine("];");

                Toucan.Core.Services.AtomicFile.WriteAllText(Path.Combine(langDir, file + ".php"), sb.ToString(), new UTF8Encoding(false));
            }
        }
    }

    public ResourceWriteResult WriteResource(ResourceWriteRequest request)
    {
        var entries = ResourceFileAdapter.Entries(request);
        var file = ResourceFilePath.Resolve(request.ProjectRoot, request.Document.Resource.RelativePath);
        var options = ResourceText.Options(request);
        var filePrefix = Path.GetFileNameWithoutExtension(file) + ".";
        if (entries.Any(i => !i.Namespace.StartsWith(filePrefix, StringComparison.Ordinal)))
            throw new FormatException($"{request.Document.Resource.RelativePath} [key]: Laravel IDs must start with '{filePrefix}'.");
        var sb = new StringBuilder();
        if (options.Indent == ResourceIndent.None) sb.Append("<?php return [");
        else { sb.AppendLine("<?php"); sb.AppendLine("return ["); }
        WriteItems(sb, entries.Where(i => ResourceFileAdapter.KeepEmpty(request) || !string.IsNullOrEmpty(i.Value)).ToList(), 1, options);
        sb.Append("];");
        if (options.Indent != ResourceIndent.None) sb.AppendLine();
        ResourceText.Write(request, sb.ToString());
        return new([]);
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));

    private static void WriteItems(StringBuilder sb, List<TranslationItem> items, int rootDepth, ResourceOptions? options = null)
    {
        // ponytail: flat write with nested grouping by dot-separated segments after the file prefix
        options ??= new() { Order = ResourceOrder.Alphabetical, Indent = ResourceIndent.Space4 };
        var tree = new Dictionary<string, object>(); // string value or nested dict

        foreach (var item in options.Order == ResourceOrder.Alphabetical ? items.OrderBy(i => i.Namespace, StringComparer.Ordinal) : items.AsEnumerable())
        {
            var parts = item.Namespace.Split('.');
            // Skip the first segment (file prefix)
            var keys = parts.Skip(1).ToArray();
            if (keys.Length == 0) continue;

            SetNested(tree, keys, item.Value ?? "");
        }

        WriteDict(sb, tree, rootDepth, options);
    }

    private static void SetNested(Dictionary<string, object> dict, string[] keys, string value)
    {
        var current = dict;
        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (!current.TryGetValue(keys[i], out var child) || child is not Dictionary<string, object> childDict)
            {
                childDict = new Dictionary<string, object>();
                current[keys[i]] = childDict;
            }
            current = (Dictionary<string, object>)current[keys[i]];
        }
        current[keys[^1]] = value;
    }

    private static void WriteDict(StringBuilder sb, Dictionary<string, object> dict, int indent, ResourceOptions options)
    {
        var pad = options.Indent == ResourceIndent.None ? "" : options.Indent == ResourceIndent.Tab ? new string('\t', indent) : new string(' ', indent * (options.Indent == ResourceIndent.Space2 ? 2 : 4));
        var newline = options.Indent == ResourceIndent.None ? "" : Environment.NewLine;
        foreach (var (key, val) in dict)
        {
            if (val is Dictionary<string, object> nested)
            {
                sb.Append($"{pad}{Quote(key)} => [").Append(newline);
                WriteDict(sb, nested, indent + 1, options);
                sb.Append($"{pad}],").Append(newline);
            }
            else
            {
                sb.Append($"{pad}{Quote(key)} => {Quote((string)val)},").Append(newline);
            }
        }
    }

    private static string Quote(string value) => value.IndexOfAny(['\n', '\r', '\t']) >= 0
        ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\""
        : "'" + Escape(value) + "'";

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
}
