using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Options;

namespace Toucan.Core.Services;

/// <param name="folder">Where project-defaults.json lives; null uses the user's Toucan folder.</param>
public class ProjectDefaultsService(string? folder = null) : IProjectDefaultsService
{
    private readonly string _folder = folder ?? Path.Combine(UserDataFolder.Root, "Toucan");

    public ProjectDefaults Load() => ProjectDefaults.LoadFromDisk(_folder);

    public void Save(ProjectDefaults defaults)
    {
        defaults.ToDisk(_folder);
    }

    public ProjectDefaults LoadOrSeed(AppOptions appOptions)
    {
        if (File.Exists(Path.Combine(_folder, "project-defaults.json")))
            return Load();

        // First run or migration: seed from existing app options
        var defaults = ProjectDefaults.SeedFrom(appOptions);
        Save(defaults);
        return defaults;
    }
}
