using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Options;

namespace Toucan.Core.Services;

public class ProjectDefaultsService : IProjectDefaultsService
{
    private static readonly string s_dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan");

    private static readonly string s_file = Path.Combine(s_dir, "project-defaults.json");

    public ProjectDefaults Load() => ProjectDefaults.LoadFromDisk();

    public void Save(ProjectDefaults defaults)
    {
        defaults.ToDisk();
    }

    public ProjectDefaults LoadOrSeed(AppOptions appOptions)
    {
        if (File.Exists(s_file))
            return Load();

        // First run or migration: seed from existing app options
        var defaults = ProjectDefaults.SeedFrom(appOptions);
        Save(defaults);
        return defaults;
    }
}
