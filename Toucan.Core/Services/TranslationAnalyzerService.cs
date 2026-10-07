using System.Text;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Services.Ai;

namespace Toucan.Core.Services;

/// <summary>
/// The Analyze AI feature: sends source and translation pairs with the application context to the AI service, using the
/// editable Analyze prompt, and reads back the problems it finds.
/// </summary>
public class TranslationAnalyzerService(IAiService ai) : ITranslationAnalyzer
{
    private const int BatchSize = 10;

    public async Task<IEnumerable<AnalysisResult>> AnalyzeAsync(AnalysisRequest request, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = request.Items.ToList();
        if (items.Count == 0) return [];

        var variables = new Dictionary<string, string?>
        {
            ["source_language"] = request.SourceLanguage,
            ["context"] = request.ApplicationContext,
            ["glossary"] = AiReviewRunner.FormatGlossary(request.Glossary),
        };

        var findings = await AiReviewRunner.RunAsync(ai, AiFeatureIds.Analyze, items, BatchSize, BuildInput, variables, request.ProjectPath,
            progress, "Analyzed", cancellationToken).ConfigureAwait(false);

        return findings.Select(f => new AnalysisResult
        {
            Namespace = f.Item.Namespace,
            TargetLanguage = f.Item.TargetLanguage,
            Severity = AiReviewRunner.Severity(f.Finding.Severity),
            Issue = f.Finding.Issue,
            SuggestedFix = f.Finding.Suggestion,
            Confidence = f.Finding.Confidence,
        }).ToList();
    }

    private static string BuildInput(IReadOnlyList<AnalysisItem> items)
    {
        var sb = new StringBuilder("Translations to review:\n");
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.AppendLine($"[{i}] key=\"{item.Namespace}\" ({item.TargetLanguage})");
            sb.AppendLine($"    source: {item.SourceText}");
            sb.AppendLine($"    translation: {item.TranslatedText}");
        }
        return sb.ToString();
    }
}
