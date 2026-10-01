using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

public interface IProjectService
{
    /// <summary>Load a project: reads toucan.project manifest + translations from folder.</summary>
    /// <remarks>Scans the folder on the calling thread; pass <paramref name="ct"/> to abort (throws <see cref="OperationCanceledException"/>).</remarks>
    ProjectLoadResult LoadProject(string folder, IProgress<ScanProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Create a new project with manifest and language files.</summary>
    ProjectSettings CreateProject(string folder, IEnumerable<string> languages, string formatId = FormatIds.Json, bool createManifest = true, string? name = null);

    /// <summary>Save translations using the project's configured style.</summary>
    void Save(ProjectSettings project, List<NsTreeItem> items, IEnumerable<TranslationItem> translations);

    /// <summary>Add a new language file to an existing project.</summary>
    void CreateLanguage(string folder, string language, string formatId = FormatIds.Json);

    /// <summary>Relative path of a language's translation file: the project's custom path if set, else the format's default.</summary>
    string GetDefaultFilePath(ProjectSettings settings, string language);

    /// <summary>Absolute paths of all files currently holding a language's data (custom path first if configured).</summary>
    IReadOnlyList<string> GetLanguageFiles(ProjectSettings settings, string language);

    // Legacy overload kept for backward compat
    List<TranslationItem> Load(string folder);
    void Save(string path, string formatId, List<NsTreeItem> items, IEnumerable<TranslationItem> translations);
}

/// <summary>Result of loading a project — contains both settings and translations.</summary>
public class ProjectLoadResult
{
    public required ProjectSettings Settings { get; init; }
    public required List<TranslationItem> Translations { get; init; }
}
