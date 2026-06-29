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
    /// <param name="saveStyle">The project's save style (determines file naming and whether sidecar is needed).</param>
    /// <param name="translations">All translation items in the project.</param>
    void SaveComments(string folder, SaveStyles saveStyle, IEnumerable<TranslationItem> translations);

    /// <summary>
    /// Loads comments from sidecar files and restores them onto matching TranslationItems.
    /// Items are matched by (Language, Namespace) composite key.
    /// Orphaned sidecar entries (namespace not in translation file) are discarded.
    /// </summary>
    /// <param name="folder">The project folder path.</param>
    /// <param name="saveStyle">The project's save style.</param>
    /// <param name="translations">All translation items to restore comments onto.</param>
    void LoadComments(string folder, SaveStyles saveStyle, IEnumerable<TranslationItem> translations);

    /// <summary>
    /// Returns true if the given save style requires sidecar comment files
    /// (i.e., does not support inline comments).
    /// </summary>
    bool RequiresSidecar(SaveStyles saveStyle);
}
