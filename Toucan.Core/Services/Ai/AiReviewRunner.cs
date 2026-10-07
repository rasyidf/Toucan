using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Ai;

/// <summary>
/// Shared flow of the review features (Analyze, Clarity): items go to the model in numbered batches, and the reply is a
/// JSON array of findings that refer back to an item by its <c>index</c> in the batch.
/// </summary>
internal static class AiReviewRunner
{
    /// <summary>One finding as the model wrote it; <see cref="Index"/> is already checked against the batch.</summary>
    public sealed record Finding(int Index, string? Severity, string Issue, string? Suggestion, string? Note, double Confidence);

    /// <summary>
    /// Runs every batch and returns the findings with the batch they refer to. A batch that fails is reported through
    /// <paramref name="progress"/>; if every batch fails, the first error is thrown. <see cref="AiUnavailableException"/>
    /// (AI off, no key) is thrown right away.
    /// </summary>
    public static async Task<List<(T Item, Finding Finding)>> RunAsync<T>(
        IAiService ai, string featureId, IReadOnlyList<T> items, int batchSize,
        Func<IReadOnlyList<T>, string> buildInput, IReadOnlyDictionary<string, string?> variables, string? projectPath,
        IProgress<PretranslationProgress>? progress, string verb, CancellationToken cancellationToken)
    {
        var results = new List<(T, Finding)>();
        if (items.Count == 0) return results;

        AiRequestException? firstError = null;
        var failed = 0;
        var processed = 0;
        var batches = items.Chunk(batchSize).ToList();
        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new AiRequest { FeatureId = featureId, Input = buildInput(batch), Variables = variables, ProjectPath = projectPath };
            string? message;
            try
            {
                var reply = await ai.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
                results.AddRange(Parse(reply, batch.Length).Select(f => (batch[f.Index], f)));
                message = null;
            }
            catch (AiRequestException ex)
            {
                firstError ??= ex;
                failed++;
                message = ex.Message;
            }

            processed += batch.Length;
            progress?.Report(new PretranslationProgress { Completed = processed, Total = items.Count, Message = message ?? $"{verb} {processed}/{items.Count}" });
        }

        if (firstError != null && failed == batches.Count) throw firstError;
        return results;
    }

    /// <summary>The findings in a reply; anything unreadable is skipped rather than guessed at.</summary>
    public static List<Finding> Parse(string raw, int count)
    {
        var findings = new List<Finding>();
        var json = AiReply.ExtractJsonArray(raw);
        if (json == null) return findings;

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var index = el.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : -1;
                if (index < 0 || index >= count) continue;
                var issue = Text(el, "issue");
                if (string.IsNullOrWhiteSpace(issue)) continue;
                var confidence = el.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0.7;
                findings.Add(new Finding(index, Text(el, "severity"), issue, Text(el, "suggestion"), Text(el, "note"), confidence));
            }
        }
        catch (JsonException) { /* not JSON after all: no findings */ }

        return findings;
    }

    private static string? Text(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s.Trim() : null;

    public static AnalysisSeverity Severity(string? value) => value?.ToLowerInvariant() switch
    {
        "error" => AnalysisSeverity.Error,
        "suggestion" or "info" => AnalysisSeverity.Suggestion,
        _ => AnalysisSeverity.Warning,
    };

    /// <summary>Glossary as prompt text: one <c>term → lang: translation, …</c> line per term.</summary>
    public static string? FormatGlossary(Dictionary<string, Dictionary<string, string>>? glossary) =>
        glossary is not { Count: > 0 } ? null
            : string.Join("\n", glossary.Select(g => $"{g.Key} → {string.Join(", ", g.Value.Select(kv => $"{kv.Key}: {kv.Value}"))}"));
}
