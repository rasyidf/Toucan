using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Persists and restores translation comments for save formats that do not support
/// inline comments. Comments are stored in JSON sidecar files alongside translation files.
/// Formats with inline comment support (Xliff, Resx, PO, AndroidXml) are handled
/// by their respective ISaveStrategy implementations.
/// </summary>
public interface ICommentPersistenceService
{
    /// <summary>
    /// Saves comments to sidecar files for formats that don't support inline comments.
    /// Groups translations by language and writes one sidecar per language file.
    /// Empty comments are excluded; comments exceeding 2000 characters are truncated.
    /// </summary>
    /// <param name="folder">The project folder path.</param>
    /// <param name="formatId">The project's format ID (determines file naming and whether a sidecar is needed).</param>
    /// <param name="translations">All translation items in the project.</param>
    void SaveComments(string folder, string formatId, IEnumerable<TranslationItem> translations);

    /// <summary>
    /// Loads comments from sidecar files and restores them onto matching TranslationItems.
    /// Items are matched by (Language, Namespace) composite key.
    /// Orphaned sidecar entries (namespace not in translation file) are discarded.
    /// </summary>
    /// <param name="folder">The project folder path.</param>
    /// <param name="formatId">The project's format ID.</param>
    /// <param name="translations">All translation items to restore comments onto.</param>
    void LoadComments(string folder, string formatId, IEnumerable<TranslationItem> translations);

    /// <summary>
    /// Returns true if the given format requires sidecar comment files
    /// (i.e., does not support inline comments).
    /// </summary>
    bool RequiresSidecar(string formatId);

    /// <summary>Sidecar files <see cref="SaveComments"/> may write or delete for these languages; empty when the format stores comments inline.</summary>
    IReadOnlyList<string> GetSidecarPaths(string folder, string formatId, IEnumerable<string> languages);
}

/// <summary>Compatibility overloads for callers that still hold a <see cref="SaveStyles"/>.</summary>
public static class CommentPersistenceServiceExtensions
{
    public static bool RequiresSidecar(this ICommentPersistenceService service, SaveStyles style) =>
        service.RequiresSidecar(FormatIds.FromStyle(style));

    public static void SaveComments(this ICommentPersistenceService service, string folder, SaveStyles style, IEnumerable<TranslationItem> translations) =>
        service.SaveComments(folder, FormatIds.FromStyle(style), translations);

    public static void LoadComments(this ICommentPersistenceService service, string folder, SaveStyles style, IEnumerable<TranslationItem> translations) =>
        service.LoadComments(folder, FormatIds.FromStyle(style), translations);
}
