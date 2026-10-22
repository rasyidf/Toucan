namespace Toucan.Core.Models;

/// <summary>
/// A single undoable edit action representing a translation value change.
/// </summary>
public record EditAction(string Namespace, string Language, string OldValue, string NewValue)
{
    public IReadOnlyList<TranslationItem>? BeforeStructure { get; init; }
    public IReadOnlyList<TranslationItem>? AfterStructure { get; init; }
}
