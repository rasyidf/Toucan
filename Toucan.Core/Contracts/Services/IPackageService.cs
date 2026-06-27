using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Manages multiple translation packages (sets) within a project.
/// A package is an independent group of translations (e.g., "ui", "errors", "emails").
/// </summary>
public interface IPackageService
{
    /// <summary>List all packages in a project.</summary>
    IEnumerable<TranslationPackage> GetPackages(ProjectSettings settings);

    /// <summary>Load translations for a specific package.</summary>
    List<TranslationItem> LoadPackage(ProjectSettings settings, string packageName);

    /// <summary>Save translations for a specific package.</summary>
    void SavePackage(ProjectSettings settings, string packageName, IEnumerable<TranslationItem> items);

    /// <summary>Create a new package in the project.</summary>
    TranslationPackage CreatePackage(ProjectSettings settings, string name);

    /// <summary>Remove a package from the project (deletes files).</summary>
    void RemovePackage(ProjectSettings settings, string packageName);

    /// <summary>Rename a package.</summary>
    void RenamePackage(ProjectSettings settings, string oldName, string newName);
}
