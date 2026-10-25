using System.Text;
using Toucan.Core.Contracts;

namespace Toucan.Core.Services;

public sealed record SourceToken(string Text, int Offset, bool Literal = false, bool Dynamic = false);

/// <summary>Bounded lexical tokens, never executes source. Comments and string contents cannot become calls.</summary>
public static class SourceSyntax
{
    public static (List<SourceToken> Tokens, List<SourceScanDiagnostic> Diagnostics) Tokenize(SourceScanFile file, bool ruby = false)
    {
        var text = file.Text;
        var tokens = new List<SourceToken>();
        var diagnostics = new List<SourceScanDiagnostic>();
        var i = 0;
        var markup = Path.GetExtension(file.File).ToLowerInvariant() is ".tsx" or ".jsx" or ".vue" or ".svelte" or ".html" or ".erb";
        var tag = false; var closingTag = false; var tagName = ""; var inText = false; var expressionDepth = 0; var attributeDepth = 0; var erb = false;
        var parents = new Stack<(string Tag, int ExpressionDepth)>();
        while (i < text.Length)
        {
            if ((i & 1023) == 0) file.CancellationToken.ThrowIfCancellationRequested();
            if (tokens.Count >= 300_000) { diagnostics.Add(new("source.token-limit", "Source token limit exceeded.", file.File)); break; }
            var c = text[i];
            if (text.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0) diagnostics.Add(new("source.unclosed-comment", "Unclosed template comment.", file.File));
                i = end < 0 ? text.Length : end + 3; continue;
            }
            if (markup && inText && text.AsSpan(i).StartsWith("<%", StringComparison.Ordinal)) { i += 2; inText = false; erb = true; continue; }
            if (erb && text.AsSpan(i).StartsWith("%>", StringComparison.Ordinal)) { i += 2; erb = false; inText = parents.Count > 0; continue; }
            if (markup && !tag && c == '<' && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] == '/' && i + 2 < text.Length && char.IsLetter(text[i + 2])))
            {
                tag = true; closingTag = text[i + 1] == '/'; attributeDepth = 0;
                var startName = i + (closingTag ? 2 : 1); var endName = startName;
                while (endName < text.Length && (char.IsLetterOrDigit(text[endName]) || text[endName] is '-' or '_')) endName++;
                tagName = text[startName..endName]; inText = false;
            }
            if (markup && inText && !tag)
            {
                if (c == '{') inText = false;
                else { i++; continue; }
            }
            if (tag && c == '{') attributeDepth++;
            else if (tag && c == '}') attributeDepth = Math.Max(0, attributeDepth - 1);
            else if (tag && c == '>' && attributeDepth == 0)
            {
                var selfClosing = i > 0 && text[i - 1] == '/' || tagName is "input" or "img" or "br" or "hr" or "meta" or "link";
                if (closingTag)
                {
                    if (parents.Count > 0) { var parent = parents.Pop(); expressionDepth = parent.ExpressionDepth; }
                }
                else if (!selfClosing) { parents.Push((tagName, expressionDepth)); expressionDepth = 0; }
                tag = false;
                inText = parents.Count > 0 && expressionDepth == 0 && parents.Peek().Tag != "script";
            }
            else if (markup && !tag && parents.Count > 0 && c == '{') expressionDepth++;
            else if (markup && !tag && parents.Count > 0 && c == '}' && expressionDepth > 0)
            { expressionDepth--; if (expressionDepth == 0 && parents.Peek().Tag != "script") inText = true; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (text.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal)) { var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal); i = end < 0 ? text.Length : end + 3; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*' )
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) diagnostics.Add(new("source.unclosed-comment", "Unclosed block comment.", file.File));
                i = end < 0 ? text.Length : end + 2; continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/' || ruby && c == '#')
            { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (c is '\'' or '"' or '`')
            {
                var start = i++; var quote = c; var value = new StringBuilder(); var closed = false; var dynamic = false;
                while (i < text.Length)
                {
                    c = text[i++];
                    if (c == quote) { closed = true; break; }
                    if (quote == '`' && c == '$' && i < text.Length && text[i] == '{' || ruby && quote == '"' && c == '#' && i < text.Length && text[i] == '{') dynamic = true;
                    if (c == '\\' && i < text.Length)
                    {
                        c = text[i++];
                        if (ruby && quote == '\'' && c is not ('\'' or '\\')) { value.Append('\\').Append(c); continue; }
                        if (c == '\n') continue;
                        if (c == '\r') { if (i < text.Length && text[i] == '\n') i++; continue; }
                        if (c == 'x' && i + 2 <= text.Length && byte.TryParse(text.AsSpan(i, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hex))
                        { value.Append((char)hex); i += 2; continue; }
                        if (c == 'u' && i < text.Length && text[i] == '{')
                        {
                            var close = text.IndexOf('}', i + 1);
                            if (close > i && int.TryParse(text.AsSpan(i + 1, close - i - 1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var scalar) && System.Text.Rune.IsValid(scalar))
                            { value.Append(char.ConvertFromUtf32(scalar)); i = close + 1; continue; }
                            dynamic = true;
                        }
                        if (c == 'u' && i + 4 <= text.Length && ushort.TryParse(text.AsSpan(i, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var unicode))
                        { value.Append((char)unicode); i += 4; continue; }
                        value.Append(c switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => c });
                    }
                    else value.Append(c);
                }
                if (!closed) diagnostics.Add(new("source.unclosed-string", "Unclosed source string.", file.File));
                tokens.Add(new(value.ToString(), start, Literal: true, Dynamic: dynamic || !closed));
                if (dynamic)
                {
                    diagnostics.Add(new("source.dynamic-template", "Interpolated string/template expressions are unresolved by the static scanner.", file.File));
                    // Nested template delimiters cannot be safely separated without a language compiler.
                    if (quote == '`' && value.ToString().Count(ch => ch == '{') > value.ToString().Count(ch => ch == '}'))
                    { diagnostics.Add(new("source.nested-template", "Scanning stopped at an unsupported nested template expression.", file.File)); break; }
                }
                continue;
            }
            // Skip regular-expression literals where an expression may begin.
            if (!ruby && c == '/' && !tag && !erb)
            {
                var start = i++; var bracket = false; var closed = false;
                while (i < text.Length && text[i] != '\n')
                {
                    c = text[i++]; if (c == '\\') { i = Math.Min(text.Length, i + 1); continue; }
                    if (c == '[') bracket = true; if (c == ']') bracket = false;
                    if (c == '/' && !bracket) { closed = true; break; }
                }
                if (!closed) diagnostics.Add(new("source.regex", "Unclosed regex or unsupported division expression.", file.File));
                tokens.Add(new("<regex>", start)); continue;
            }
            if (char.IsLetterOrDigit(c) || c is '_' or '$')
            {
                var start = i++; while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$')) i++;
                tokens.Add(new(text[start..i], start)); continue;
            }
            tokens.Add(new(c.ToString(), i++));
        }
        if (tag || parents.Count > 0) diagnostics.Add(new("source.unclosed-template", "Unclosed or unsupported template structure.", file.File));
        return (tokens, diagnostics);
    }

    public static SourceReference Reference(SourceScanFile file, SourceToken token, string key, string? package)
    {
        var lo = 0; var hi = file.LineStarts.Count - 1;
        while (lo <= hi) { var mid = lo + (hi - lo) / 2; if (file.LineStarts[mid] <= token.Offset) lo = mid + 1; else hi = mid - 1; }
        var index = Math.Max(0, hi);
        var line = index + 1; var column = token.Offset - file.LineStarts[index] + 1;
        return new(key, package, file.File, token.Offset, line, column, Models.ResourceEntryOperations.ParseSelector(key), file.Text[file.LineStarts[index]..(index + 1 < file.LineStarts.Count ? file.LineStarts[index + 1] : file.Text.Length)].TrimEnd('\r', '\n'));
    }
}
