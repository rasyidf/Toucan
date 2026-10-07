using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>Loads PO/POT (gettext) translation files.</summary>
/// <remarks>
/// An entry's key is <c>msgid</c>, or <c>msgctxt</c> + <see cref="PoFormat.ContextSeparator"/> + <c>msgid</c> when it has a context.
/// <see cref="TranslationItem.Value"/> is <c>msgstr</c> (or <c>msgstr[0]</c> for plurals); everything else needed to write the
/// entry back goes in <see cref="TranslationItem.FormatData"/> under the <see cref="PoFormat"/> keys.
/// </remarks>
public partial class PoLoadStrategy : ILoadStrategy
{
    public string FormatId => FormatIds.Po;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = Directory.GetFiles(folder, "*.po", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folder, "*.pot", SearchOption.AllDirectories));

        var items = new List<TranslationItem>();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var language = ResolveLanguage(folder, file, content);
            items.AddRange(ParsePo(language, Path.GetRelativePath(folder, file), content));
        }
        return items;
    }

    /// <summary>Language from the <c>Language:</c> header, then a <c>&lt;lang&gt;/LC_MESSAGES/</c> folder, then the file name.</summary>
    internal static string ResolveLanguage(string root, string file, string content)
    {
        var header = HeaderLanguageRegex().Match(content);
        if (header.Success && header.Groups[1].Value.Trim() is { Length: > 0 } fromHeader)
            return fromHeader;

        var dir = Path.GetDirectoryName(file);
        if (dir != null && Path.GetFileName(dir).Equals("LC_MESSAGES", StringComparison.OrdinalIgnoreCase))
        {
            var langDir = Path.GetFileName(Path.GetDirectoryName(dir));
            if (!string.IsNullOrEmpty(langDir)) return langDir;
        }

        return Path.GetFileNameWithoutExtension(file);
    }

    [GeneratedRegex(@"^""Language:\s*([^\\""\r\n]*)", RegexOptions.Multiline)]
    private static partial Regex HeaderLanguageRegex();

    [GeneratedRegex(@"^msgstr\[(\d+)\]\s*(.*)$")]
    private static partial Regex PluralStrRegex();

    private sealed class Entry
    {
        public string? Ctxt, Id, IdPlural, Str;
        public readonly SortedDictionary<int, string> PluralStrs = [];
        public readonly List<string> Comments = [], Extracted = [], Refs = [], Flags = [];
        public bool Obsolete;
        public bool HasContent => Id != null && (Str != null || PluralStrs.Count > 0);
    }

    private enum Field { None, Ctxt, Id, IdPlural, Str, PluralStr }

    internal static List<TranslationItem> ParsePo(string language, string relativePath, string content)
    {
        var entries = new List<(Entry Entry, string? Header)>();
        string? header = null, headerComment = null;
        var cur = new Entry();
        var field = Field.None;
        var pluralIndex = 0;

        void Flush()
        {
            if (cur.HasContent && !cur.Obsolete)
            {
                if (string.IsNullOrEmpty(cur.Id) && cur.Ctxt == null)
                {
                    header = cur.Str ?? "";
                    headerComment = string.Join('\n', cur.Comments);
                }
                else
                    entries.Add((cur, null));
            }
            cur = new Entry();
            field = Field.None;
        }

        void Append(string text)
        {
            switch (field)
            {
                case Field.Ctxt: cur.Ctxt += text; break;
                case Field.Id: cur.Id += text; break;
                case Field.IdPlural: cur.IdPlural += text; break;
                case Field.Str: cur.Str += text; break;
                case Field.PluralStr: cur.PluralStrs[pluralIndex] += text; break;
            }
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0) { Flush(); continue; }

            if (trimmed.StartsWith("#~")) { cur.Obsolete = true; continue; }

            if (trimmed[0] == '#')
            {
                // A comment after msgstr starts the next entry.
                if (field is Field.Str or Field.PluralStr) Flush();
                if (trimmed.StartsWith("#:")) cur.Refs.Add(trimmed[2..].Trim());
                else if (trimmed.StartsWith("#,"))
                    cur.Flags.AddRange(trimmed[2..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                else if (trimmed.StartsWith("#.")) cur.Extracted.Add(trimmed[2..].TrimStart());
                else if (trimmed.StartsWith("#|")) { /* previous msgid: not kept */ }
                else cur.Comments.Add(trimmed.Length > 1 ? trimmed[1..].TrimStart() : "");
                continue;
            }

            if (trimmed.StartsWith("msgctxt "))
            {
                if (field is Field.Str or Field.PluralStr) Flush();
                cur.Ctxt = Unquote(trimmed[8..]);
                field = Field.Ctxt;
            }
            else if (trimmed.StartsWith("msgid_plural "))
            {
                cur.IdPlural = Unquote(trimmed[13..]);
                field = Field.IdPlural;
            }
            else if (trimmed.StartsWith("msgid "))
            {
                if (field is Field.Str or Field.PluralStr) Flush();
                cur.Id = Unquote(trimmed[6..]);
                field = Field.Id;
            }
            else if (trimmed.StartsWith("msgstr["))
            {
                var m = PluralStrRegex().Match(trimmed);
                if (!m.Success) continue;
                pluralIndex = int.Parse(m.Groups[1].Value);
                cur.PluralStrs[pluralIndex] = Unquote(m.Groups[2].Value);
                field = Field.PluralStr;
            }
            else if (trimmed.StartsWith("msgstr "))
            {
                cur.Str = Unquote(trimmed[7..]);
                field = Field.Str;
            }
            else if (trimmed[0] == '"')
            {
                Append(Unquote(trimmed));
            }
        }
        Flush();

        var result = new List<TranslationItem>(entries.Count);
        foreach (var (e, _) in entries)
        {
            var key = e.Ctxt != null ? e.Ctxt + PoFormat.ContextSeparator + e.Id : e.Id!;
            if (key.Length == 0) continue;

            var isPlural = e.IdPlural != null;
            var data = new Dictionary<string, string> { [PoFormat.File] = relativePath };
            if (header != null) data[PoFormat.Header] = header;
            if (!string.IsNullOrEmpty(headerComment)) data[PoFormat.HeaderComment] = headerComment;
            if (isPlural) data[PoFormat.IdPlural] = e.IdPlural!;
            foreach (var (n, text) in e.PluralStrs.Where(kv => kv.Key > 0)) data[PoFormat.PluralPrefix + n] = text;
            if (e.Refs.Count > 0) data[PoFormat.Refs] = string.Join('\n', e.Refs);
            if (e.Extracted.Count > 0) data[PoFormat.Extracted] = string.Join('\n', e.Extracted);
            data[PoFormat.Flags] = string.Join(", ", e.Flags.Where(f => f != "fuzzy"));
            var fuzzy = e.Flags.Contains("fuzzy");
            if (fuzzy) data[PoFormat.Fuzzy] = "1";

            result.Add(new TranslationItem
            {
                Language = language,
                Namespace = key,
                Value = isPlural ? e.PluralStrs.GetValueOrDefault(0, "") : e.Str ?? "",
                Comment = string.Join('\n', e.Comments),
                IsApproved = !fuzzy,
                FormatData = data,
            });
        }

        // A file with only a header still needs to remember its path and header.
        return result;
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        if (!s.Contains('\\')) return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            var next = s[++i];
            sb.Append(next switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', _ => next });
            if (next is not ('n' or 't' or 'r' or '"' or '\\')) sb.Insert(sb.Length - 1, '\\');
        }
        return sb.ToString();
    }
}

/// <summary>Keys used in <see cref="TranslationItem.FormatData"/> for PO entries, and the key separator.</summary>
public static class PoFormat
{
    /// <summary>gettext's own separator between <c>msgctxt</c> and <c>msgid</c>.</summary>
    public const char ContextSeparator = '\u0004';

    public const string File = "po.file";
    public const string Header = "po.header";
    public const string HeaderComment = "po.header_comment";
    public const string IdPlural = "po.msgid_plural";
    public const string PluralPrefix = "po.msgstr.";
    public const string Refs = "po.refs";
    public const string Extracted = "po.extracted";
    public const string Flags = "po.flags";
    public const string Fuzzy = "po.fuzzy";
}
