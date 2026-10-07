using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services.LoadStrategies;

/// <summary>
/// Loads Laravel language files (<c>lang/&lt;locale&gt;/&lt;file&gt;.php</c> returning an array). Reads string values, including
/// multiline and double-quoted ones; computed values (function calls, constants, numbers) are skipped.
/// </summary>
public class LaravelPhpLoadStrategy : ILoadStrategy
{
    public string FormatId => FormatIds.LaravelPhp;

    public IEnumerable<TranslationItem> Load(string folder)
    {
        var files = FileEnumerator.EnumerateFiles(folder, "*.php");
        var items = new List<TranslationItem>();

        foreach (var file in files)
        {
            // Language is the parent directory name (e.g., "en", "fr", "pt-BR")
            var lang = Path.GetFileName(Path.GetDirectoryName(file)!);
            var fileBase = Path.GetFileNameWithoutExtension(file);
            new Parser(File.ReadAllText(file), lang, items).Run(fileBase);
        }
        return items;
    }

    private sealed class Parser(string text, string lang, List<TranslationItem> items)
    {
        private int _pos;

        public void Run(string prefix)
        {
            var ret = text.IndexOf("return", StringComparison.Ordinal);
            if (ret < 0) return;
            _pos = ret + "return".Length;
            SkipTrivia();
            if (Peek() == '[') { _pos++; ParseArray(prefix); }
            else if (text.AsSpan(_pos).StartsWith("array", StringComparison.OrdinalIgnoreCase))
            {
                _pos = text.IndexOf('(', _pos);
                if (_pos >= 0) { _pos++; ParseArray(prefix); }
            }
        }

        private char Peek() => _pos < text.Length ? text[_pos] : '\0';

        private void ParseArray(string prefix)
        {
            var index = 0;
            while (true)
            {
                SkipTrivia();
                var c = Peek();
                if (c == '\0') return;
                if (c is ']' or ')') { _pos++; return; }
                if (c == ',') { _pos++; continue; }

                string key;
                string? first = ReadScalar(out var firstWasString);
                SkipTrivia();
                if (text.AsSpan(_pos).StartsWith("=>"))
                {
                    _pos += 2;
                    key = first ?? string.Empty;
                    SkipTrivia();
                }
                else
                {
                    // List entry with no key: the value already read is the value; key is its index.
                    key = index.ToString();
                    index++;
                    if (first != null && firstWasString) Add(prefix, key, first);
                    continue;
                }

                var path = prefix + "." + key;
                if (Peek() == '[') { _pos++; ParseArray(path); }
                else if (text.AsSpan(_pos).StartsWith("array", StringComparison.OrdinalIgnoreCase) && NextNonSpace(_pos + 5) == '(')
                {
                    _pos = text.IndexOf('(', _pos) + 1;
                    ParseArray(path);
                }
                else
                {
                    var value = ReadScalar(out var isString);
                    if (isString && value != null) Add(prefix, key, value);
                }
            }
        }

        private char NextNonSpace(int from)
        {
            while (from < text.Length && char.IsWhiteSpace(text[from])) from++;
            return from < text.Length ? text[from] : '\0';
        }

        private void Add(string prefix, string key, string value) =>
            items.Add(new TranslationItem { Language = lang, Namespace = prefix + "." + key, Value = value });

        /// <summary>Reads a quoted string, or skips a non-string expression up to the next top-level ',' or closing bracket.</summary>
        private string? ReadScalar(out bool isString)
        {
            var c = Peek();
            if (c is '\'' or '"') { isString = true; return ReadQuoted(c); }

            isString = false;
            var start = _pos;
            var depth = 0;
            while (_pos < text.Length)
            {
                var ch = text[_pos];
                if (ch is '\'' or '"') { ReadQuoted(ch); continue; }
                if (ch is '(' or '[') depth++;
                else if (ch is ')' or ']') { if (depth == 0) break; depth--; }
                else if (ch == ',' && depth == 0) break;
                else if (ch == '=' && depth == 0 && _pos + 1 < text.Length && text[_pos + 1] == '>') break;
                _pos++;
            }
            // A bare number used as a key.
            return text[start.._pos].Trim() is { Length: > 0 } raw ? raw : null;
        }

        private string ReadQuoted(char quote)
        {
            _pos++; // opening quote
            var sb = new StringBuilder();
            while (_pos < text.Length && text[_pos] != quote)
            {
                var ch = text[_pos];
                if (ch == '\\' && _pos + 1 < text.Length)
                {
                    var next = text[_pos + 1];
                    if (quote == '\'')
                    {
                        if (next is '\\' or '\'') { sb.Append(next); _pos += 2; continue; }
                    }
                    else
                    {
                        var mapped = next switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'v' => '\v', 'e' => '\u001b', 'f' => '\f', '\\' => '\\', '$' => '$', '"' => '"', _ => '\0' };
                        if (mapped != '\0') { sb.Append(mapped); _pos += 2; continue; }
                    }
                }
                sb.Append(ch);
                _pos++;
            }
            _pos++; // closing quote
            return sb.ToString();
        }

        private void SkipTrivia()
        {
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (char.IsWhiteSpace(c)) { _pos++; continue; }
                if (c == '#' || (c == '/' && _pos + 1 < text.Length && text[_pos + 1] == '/'))
                {
                    while (_pos < text.Length && text[_pos] != '\n') _pos++;
                    continue;
                }
                if (c == '/' && _pos + 1 < text.Length && text[_pos + 1] == '*')
                {
                    var end = text.IndexOf("*/", _pos + 2, StringComparison.Ordinal);
                    _pos = end < 0 ? text.Length : end + 2;
                    continue;
                }
                break;
            }
        }
    }
}
