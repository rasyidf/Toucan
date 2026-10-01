using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Writes translations in one file format. A strategy also owns the format's file-layout conventions
/// (default path, per-language files, comment storage, detection), so nothing else needs to switch on the format.
/// </summary>
public interface ISaveStrategy
{
    /// <summary>Stable format identifier (see <see cref="FormatIds"/>).</summary>
    string FormatId { get; }

    /// <summary>Human-readable name for format pickers.</summary>
    string DisplayName => FormatId;

    /// <summary>File extensions (with dot) that map to this format when exporting by file name. Empty if none.</summary>
    IReadOnlyList<string> FileExtensions => [];

    /// <summary>Default translation file path for a language, relative to the project root, using '/' separators.</summary>
    string DefaultFilePath(string language);

    /// <summary>All files that currently hold a language's data. Defaults to the single default path.</summary>
    IReadOnlyList<string> LanguageFiles(string projectRoot, string language) =>
        [Path.Combine(projectRoot, DefaultFilePath(language).Replace('/', Path.DirectorySeparatorChar))];

    /// <summary>True when the format stores comments in the translation files themselves (no sidecar needed).</summary>
    bool StoresCommentsInline => false;

    /// <summary>Path (relative to the project root) the comments sidecar is placed next to.</summary>
    string CommentSidecarBase(string language) =>
        DefaultFilePath(language).Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Folder-scan detection rule, or null if the format is never auto-detected.</summary>
    FormatDetection? Detection => null;

    void Save(string path, SaveContext context);
    Task SaveAsync(string path, SaveContext context);
}
