using System.Reflection;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Ai;

/// <summary>The AI features that ship with Toucan. Their default prompts are the files in <c>Toucan.Core/Ai/Prompts</c>.</summary>
public static class BuiltInAiFeatures
{
    private static readonly AiPromptVariable SourceLanguage = new("source_language", "Language code of the source text, e.g. en-US");
    private static readonly AiPromptVariable TargetLanguage = new("target_language", "Language code to translate into, e.g. fr-FR");
    private static readonly AiPromptVariable Context = new("context", "Application context from Settings or Project Properties");
    private static readonly AiPromptVariable Formality = new("formality", "Formality from Settings or Project Properties (empty for Default)");
    private static readonly AiPromptVariable Glossary = new("glossary", "Approved terms, one per line (empty when there is no glossary)");

    public static IReadOnlyList<AiFeatureDefinition> All { get; } =
    [
        new()
        {
            Id = AiFeatureIds.Translate,
            DisplayName = "Translate",
            Description = "Machine translation with the AI provider: in the Translation panel, Pre-translate and quick translate.",
            DefaultPrompt = Read(AiFeatureIds.Translate),
            Variables = [SourceLanguage, TargetLanguage, Context, Formality, Glossary],
            ResponseFormat = "The input is a JSON array of texts. The reply must be a JSON array of translations, same order and length.",
            Temperature = 0.3,
            JsonResponse = true,
        },
        new()
        {
            Id = AiFeatureIds.Analyze,
            DisplayName = "Analyze",
            Description = "Reviews translations against the source for wrong terms, placeholders, tone and grammar. Findings go to the Issues panel.",
            DefaultPrompt = Read(AiFeatureIds.Analyze),
            Variables = [SourceLanguage, Context, Glossary],
            ResponseFormat = "The input lists numbered items. The reply must be a JSON array of {index, severity, issue, suggestion, confidence} for the items with a problem, or [].",
            Temperature = 0.2,
            JsonResponse = true,
        },
        new()
        {
            Id = AiFeatureIds.Clarity,
            DisplayName = "Clarity",
            Description = "Reviews source strings for ambiguity, missing context and wording translators could misread. Findings go to the Issues panel.",
            DefaultPrompt = Read(AiFeatureIds.Clarity),
            Variables = [SourceLanguage, Context],
            ResponseFormat = "The input lists numbered items. The reply must be a JSON array of {index, severity, issue, suggestion, note, confidence} for the strings with a problem, or [].",
            Temperature = 0.2,
            JsonResponse = true,
        },
    ];

    /// <summary>A built-in prompt file's text, as shipped.</summary>
    public static string Read(string featureId)
    {
        var name = $"Toucan.Core.Ai.Prompts.{featureId}.md";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Built-in prompt '{name}' is missing from Toucan.Core.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n").Trim();
    }
}
