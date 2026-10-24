namespace Toucan.Core.Models;

/// <summary>
/// A single undoable edit action representing a translation value change.
/// </summary>
public record EditAction(string Namespace, string Language, string OldValue, string NewValue)
{
    public TranslationItem? BeforeEntry { get; init; }
    public TranslationItem? AfterEntry { get; init; }
    public IReadOnlyList<TranslationItem>? BeforeStructure { get; init; }
    public IReadOnlyList<TranslationItem>? AfterStructure { get; init; }
    public Dictionary<string, string>? BeforeMetadata { get; init; }
    public Dictionary<string, string>? AfterMetadata { get; init; }
    public SpellingSettings? BeforeSpelling { get; init; }
    public SpellingSettings? AfterSpelling { get; init; }
    public ProjectSettings? BeforeConfiguration { get; init; }
    public ProjectSettings? AfterConfiguration { get; init; }
}
