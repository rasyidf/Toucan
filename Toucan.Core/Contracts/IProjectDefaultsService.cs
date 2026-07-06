using Toucan.Core.Options;

namespace Toucan.Core.Contracts;

/// <summary>
/// Manages the ProjectDefaults template (load/save/seed from existing AppOptions).
/// </summary>
public interface IProjectDefaultsService
{
    ProjectDefaults Load();
    void Save(ProjectDefaults defaults);

    /// <summary>
    /// Ensures project-defaults.json exists. If not, seeds from current AppOptions.
    /// Called once at startup.
    /// </summary>
    ProjectDefaults LoadOrSeed(AppOptions appOptions);
}
