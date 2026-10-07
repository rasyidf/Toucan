using System.Text;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Services.Ai;

namespace Toucan.Core.Services;

/// <summary>The Clarity AI feature; see <see cref="ISourceClarityService"/>.</summary>
public sealed class SourceClarityService(IAiService ai) : ISourceClarityService
{
    private const int BatchSize = 25;

    public async Task<IReadOnlyList<ClarityResult>> ReviewAsync(ClarityRequest request, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = request.Items.Where(i => !string.IsNullOrWhiteSpace(i.SourceText)).ToList();
        if (items.Count == 0) return [];

        var variables = new Dictionary<string, string?>
        {
            ["source_language"] = request.SourceLanguage,
            ["context"] = request.ApplicationContext,
        };

        var findings = await AiReviewRunner.RunAsync(ai, AiFeatureIds.Clarity, items, BatchSize, BuildInput, variables, request.ProjectPath,
            progress, "Reviewed", cancellationToken).ConfigureAwait(false);

        return [.. findings.Select(f => new ClarityResult
        {
            Namespace = f.Item.Namespace,
            SourceText = f.Item.SourceText,
            Severity = AiReviewRunner.Severity(f.Finding.Severity),
            Issue = f.Finding.Issue,
            // A "suggestion" equal to the source is no suggestion.
            SuggestedSource = f.Finding.Suggestion is { } s && !string.Equals(s, f.Item.SourceText, StringComparison.Ordinal) ? s : null,
            TranslatorNote = f.Finding.Note,
            Confidence = f.Finding.Confidence,
        })];
    }

    private static string BuildInput(IReadOnlyList<ClarityItem> items)
    {
        var sb = new StringBuilder("Source strings to review:\n");
        for (var i = 0; i < items.Count; i++)
        {
            sb.AppendLine($"[{i}] key=\"{items[i].Namespace}\"");
            sb.AppendLine($"    text: {items[i].SourceText}");
        }
        return sb.ToString();
    }
}
