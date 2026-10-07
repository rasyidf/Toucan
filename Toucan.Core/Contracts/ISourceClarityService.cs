using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

/// <summary>
/// The Clarity AI feature: reviews source-language strings before translation for ambiguity ("Clear", "Order"), strings
/// too short to translate without context, concatenation, idioms and unclear placeholders. It suggests a clearer source
/// string and a note for translators. Requests go through <see cref="IAiService"/> with the editable Clarity prompt.
/// </summary>
public interface ISourceClarityService
{
    Task<IReadOnlyList<ClarityResult>> ReviewAsync(ClarityRequest request, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class ClarityRequest
{
    /// <summary>Source strings to review: key and text in the source language.</summary>
    public required IReadOnlyList<ClarityItem> Items { get; init; }
    public string SourceLanguage { get; init; } = "en-US";
    public string? ApplicationContext { get; init; }

    /// <summary>Open project folder, so the project's own Clarity prompt is used when it has one.</summary>
    public string? ProjectPath { get; init; }
}

public sealed record ClarityItem(string Namespace, string SourceText);

public sealed class ClarityResult
{
    public required string Namespace { get; init; }
    public required string SourceText { get; init; }
    public required AnalysisSeverity Severity { get; init; }
    public required string Issue { get; init; }

    /// <summary>A clearer source string, or null when the wording is fine but translators need context.</summary>
    public string? SuggestedSource { get; init; }

    /// <summary>Context for translators, suitable as a key comment.</summary>
    public string? TranslatorNote { get; init; }

    public double Confidence { get; init; }
}
