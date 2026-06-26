using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

public interface IProjectService
{
    /// <summary>Load a project: reads toucan.project manifest + translations from folder.</summary>
    ProjectLoadResult LoadProject(string folder);

    /// <summary>Create a new project with manifest and language files.</summary>
    ProjectSettings CreateProject(string folder, IEnumerable<string> languages, SaveStyles style = SaveStyles.Json, bool createManifest = true, string? name = null);

    /// <summary>Save translations using the project's configured style.</summary>
    void Save(ProjectSettings project, List<NsTreeItem> items, IEnumerable<TranslationItem> translations);

    /// <summary>Add a new language file to an existing project.</summary>
    void CreateLanguage(string folder, string language, SaveStyles style = SaveStyles.Json);

    // Legacy overload kept for backward compat
    List<TranslationItem> Load(string folder);
    void Save(string path, SaveStyles style, List<NsTreeItem> items, IEnumerable<TranslationItem> translations);
}

/// <summary>Result of loading a project — contains both settings and translations.</summary>
public class ProjectLoadResult
{
    public required ProjectSettings Settings { get; init; }
    public required List<TranslationItem> Translations { get; init; }
}
