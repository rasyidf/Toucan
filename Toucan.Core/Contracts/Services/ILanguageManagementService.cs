namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Manages language lifecycle operations including adding, removing, and reordering languages
/// within a translation project.
/// </summary>
public interface ILanguageManagementService
{
    /// <summary>Adds a language: creates empty TranslationItems for all namespaces and writes the language file.</summary>
    Task<LanguageOperationResult> AddLanguageAsync(string languageCode, CancellationToken ct = default);

    /// <summary>Removes a language: deletes items from memory, files from disk, updates manifest.</summary>
    Task<LanguageOperationResult> RemoveLanguageAsync(string languageCode, CancellationToken ct = default);

    /// <summary>Gets the file paths that would be deleted for a language removal (for confirmation UI).</summary>
    IReadOnlyList<string> GetLanguageFilePaths(string languageCode);

    /// <summary>Reorders languages in the manifest.</summary>
    Task ReorderLanguagesAsync(IReadOnlyList<string> orderedLanguages, CancellationToken ct = default);
}

/// <summary>Result of a language add or remove operation.</summary>
public record LanguageOperationResult(bool Success, string? ErrorMessage = null, IReadOnlyList<string>? FailedPaths = null);
