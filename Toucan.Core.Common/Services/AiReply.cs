using System.Text.Json;

namespace Toucan.Core.Services;

/// <summary>Reads JSON out of model replies, which often wrap it in a Markdown code fence or a sentence.</summary>
public static class AiReply
{
    /// <summary>The reply without a surrounding <c>```json … ```</c> fence.</summary>
    public static string StripCodeFence(string raw)
    {
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
        if (trimmed.EndsWith("```", StringComparison.Ordinal)) trimmed = trimmed[..^3];
        return trimmed.Trim();
    }

    /// <summary>
    /// The first JSON array in the reply: the whole reply after removing a code fence, or failing that the text between the
    /// first '[' and the last ']'. Null when there is none.
    /// </summary>
    public static string? ExtractJsonArray(string raw)
    {
        var text = StripCodeFence(raw);
        if (text.StartsWith('[')) return text;
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        return start >= 0 && end > start ? text[start..(end + 1)] : null;
    }

    /// <summary>
    /// A JSON array of strings, one per input in the same order. A single expected value falls back to the raw reply;
    /// otherwise an unreadable reply gives nulls, so every input is reported as untranslated rather than misaligned.
    /// </summary>
    public static List<string?> ParseStringArray(string raw, int expected)
    {
        var json = ExtractJsonArray(raw);
        if (json != null)
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string?[]>(json);
                if (arr != null) return [.. arr];
            }
            catch (JsonException) { /* fall through */ }
        }

        return expected == 1 ? [StripCodeFence(raw)] : [.. Enumerable.Repeat<string?>(null, expected)];
    }
}
