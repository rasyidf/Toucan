using System.Text.RegularExpressions;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>UTF-16 spans of natural text, excluding message syntax but retaining ICU branch prose.</summary>
public static partial class SpellingText
{
    public static IReadOnlyList<SpellingWord> Words(string text)
    {
        if (text.Length > 65536) throw new FormatException("Spelling checks support up to 65,536 characters per translation.");
        var masked = new bool[text.Length];
        foreach (Match match in Syntax().Matches(text)) Mask(match.Index, match.Length);
        var pairs = new int[text.Length]; Array.Fill(pairs, -1);
        var openings = new Stack<int>();
        for (var at = 0; at < text.Length; at++)
        {
            if (masked[at]) continue;
            if (text[at] == '\'' && at + 1 < text.Length && text[at + 1] is '{' or '}')
            {
                var endQuote = text.IndexOf('\'', at + 2);
                if (endQuote >= 0) { at = endQuote; continue; }
            }
            if (text[at] == '{') openings.Push(at);
            else if (text[at] == '}' && openings.Count > 0) pairs[openings.Pop()] = at;
        }
        Scan(0, text.Length, 0);
        var result = new List<SpellingWord>();
        foreach (Match match in Word().Matches(text))
        {
            if (result.Count >= 8192) throw new FormatException("Spelling word limit reached.");
            if (match.Index > 0 && text[match.Index - 1] == ':' && (match.Index < 2 || !char.IsLetterOrDigit(text[match.Index - 2]))) continue;
            if (match.Length > 128 || Enumerable.Range(match.Index, match.Length).Any(i => masked[i])) continue;
            result.Add(new(match.Index, match.Length, match.Value));
        }
        return result;
        void Mask(int start, int length) { Array.Fill(masked, true, start, length); }
        void Scan(int start, int end, int depth)
        {
            if (depth > 24) throw new FormatException("ICU nesting exceeds the spelling limit.");
            for (var i = start; i < end; i++)
            {
                if (masked[i]) continue;
                if (text[i] == '\'' && i + 1 < end && text[i + 1] is '{' or '}')
                {
                    var quoteEnd = text.IndexOf('\'', i + 2);
                    if (quoteEnd >= 0) { i = quoteEnd; continue; }
                }
                if (text[i] != '{') continue;
                if (depth == 0 && i + 1 < end && text[i + 1] == '{')
                {
                    var doubleEnd = pairs[i] >= 0 && pairs[i + 1] == pairs[i] - 1 ? pairs[i] - 1 : -1;
                    if (doubleEnd >= 0 && !text.AsSpan(i + 2, doubleEnd - i - 2).ContainsAny('{', '}')) { Mask(i, doubleEnd - i + 2); i = doubleEnd + 1; continue; }
                }
                var closing = Close(i, end);
                if (closing < 0) continue;
                var at = i + 1; Space(ref at, closing);
                var nameStart = at; while (at < closing && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;
                if (at == nameStart) continue;
                Space(ref at, closing);
                if (at == closing) { Mask(i, closing - i + 1); i = closing; continue; }
                if (text[at] == ':') { Mask(i, closing - i + 1); i = closing; continue; }
                if (text[at] != ',') continue;
                at++; Space(ref at, closing); var typeStart = at; while (at < closing && char.IsLetter(text[at])) at++;
                var type = text[typeStart..at];
                if (type is not ("plural" or "select" or "selectordinal")) { Mask(i, closing - i + 1); i = closing; continue; }
                if (at >= closing || text[at] != ',') throw new FormatException("Malformed ICU options; spelling is unavailable for this message.");
                at++; var syntaxStart = i;
                while (at < closing)
                {
                    while (at < closing && text[at] != '{') at++;
                    if (at >= closing) break;
                    var branchEnd = Close(at, closing);
                    Mask(syntaxStart, at - syntaxStart + 1);
                    if (branchEnd < 0) throw new FormatException("Unbalanced ICU branch; spelling is unavailable for this message.");
                    Scan(at + 1, branchEnd, depth + 1); masked[branchEnd] = true;
                    at = branchEnd + 1; syntaxStart = at;
                }
                Mask(syntaxStart, closing - syntaxStart + 1); i = closing;
            }
        }
        int Close(int opening, int end) => pairs[opening] < end ? pairs[opening] : -1;
        void Space(ref int at, int end) { while (at < end && char.IsWhiteSpace(text[at])) at++; }
    }
    [GeneratedRegex(@"<[^<>]*>|&(?:#[0-9]{1,8}|#x[0-9A-Fa-f]{1,8}|[A-Za-z][A-Za-z0-9]{0,31});|https?://[^\s<>]+|\$\{[^{}]*\}|%\([^)\r\n]{1,128}\)[a-zA-Z]|%[-+ #0]*[0-9]*(?:\.[0-9]+)?(?:[0-9]+\$)?(?:hh|ll|h|l|j|z|t|L)?[sdifFeEgGaAcCuoxXn@]|%[0-9]+|`[^`]*`", RegexOptions.NonBacktracking)]
    private static partial Regex Syntax();
    [GeneratedRegex(@"[\p{L}\p{M}]+(?:['’][\p{L}\p{M}]+)*", RegexOptions.NonBacktracking)] private static partial Regex Word();
}
