using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

/// <summary>
/// Context-aware translation quality analysis (the Analyze AI feature).
/// Uses application domain context (banking, education, medical, etc.) to verify
/// that translations use correct domain-specific terminology.
///
/// Example: "Hold" in banking = freeze funds, not physical hold.
/// Requests go through <see cref="IAiService"/> with the editable Analyze prompt, so they follow the app-wide AI switch.
/// </summary>
public interface ITranslationAnalyzer
{
    /// <summary>Analyze a batch of translations for quality issues given application context.</summary>
    Task<IEnumerable<AnalysisResult>> AnalyzeAsync(AnalysisRequest request, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default);
}

public class AnalysisRequest
{
    /// <summary>Items to analyze (source + translation pairs).</summary>
    public required IEnumerable<AnalysisItem> Items { get; init; }

    /// <summary>Application domain context provided by the user (e.g., "This is a banking app for managing savings accounts and transactions").</summary>
    public required string ApplicationContext { get; init; }

    /// <summary>Optional glossary: domain-specific term → expected translation per language.</summary>
    public Dictionary<string, Dictionary<string, string>>? Glossary { get; init; }

    /// <summary>Open project folder, so the project's own Analyze prompt is used when it has one.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>Source language code.</summary>
    public string SourceLanguage { get; init; } = "en-US";
}

public record AnalysisItem(string Namespace, string SourceText, string TranslatedText, string TargetLanguage);

public class AnalysisResult
{
    public required string Namespace { get; init; }
    public required string TargetLanguage { get; init; }
    public required AnalysisSeverity Severity { get; init; }
    public required string Issue { get; init; }
    public string? SuggestedFix { get; init; }
    /// <summary>Confidence score (0..1) that this is actually wrong.</summary>
    public double Confidence { get; init; }
}

public enum AnalysisSeverity { Error, Warning, Suggestion }
