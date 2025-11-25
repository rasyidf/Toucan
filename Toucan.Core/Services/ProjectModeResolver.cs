using System.IO;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services
{
    public class ProjectModeResolver : IProjectModeResolver
    {
        private const string ManifestFileName = "toucan.project";

        public ProjectTypeVariant Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return ProjectTypeVariant.FolderScan;

            var manifestPath = Path.Combine(path, ManifestFileName);
            if (File.Exists(manifestPath)) return ProjectTypeVariant.ConfigManifest;
            return ProjectTypeVariant.FolderScan;
        }
    }
}
