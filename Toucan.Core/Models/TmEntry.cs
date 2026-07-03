namespace Toucan.Core.Models;

/// <summary>
/// A single translation memory entry for TMX import/export and TM management.
/// </summary>
public record TmEntry(
    string SourceLang,
    string SourceText,
    string TargetLang,
    string TargetText,
    DateTime? CreatedDate);
