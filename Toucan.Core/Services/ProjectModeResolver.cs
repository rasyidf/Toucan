using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public class ProjectModeResolver : IProjectModeResolver
{
    public ProjectTypeVariant Resolve(string path) =>
        !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "toucan.project"))
            ? ProjectTypeVariant.ConfigManifest
            : ProjectTypeVariant.FolderScan;
}
