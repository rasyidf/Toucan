namespace Toucan.Core.Services;

public static class ResourceFilePath
{
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new ArgumentException("An explicit resource needs a relative destination.", nameof(relative));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Resource destination must stay within the project.", nameof(relative));
        // Resolve existing ancestors, including junctions, before IO. New files still have a physical parent.
        var physicalRoot = Physical(fullRoot);
        var physical = Physical(path);
        if (!physical.StartsWith(Path.TrimEndingDirectorySeparator(physicalRoot) + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Resource destination resolves outside the project. Copy external resources into the project first.", nameof(relative));
        return physical;
    }

    private static string Physical(string path, int remainingLinks = 64)
    {
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if ((info.Exists || info.LinkTarget != null) && (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is null || !target.Exists) throw new ArgumentException("Resource path contains an unresolved link.");
                if (remainingLinks == 0) throw new ArgumentException("Resource path contains too many linked ancestors.");
                // ResolveLinkTarget resolves the link itself, but its target may still contain
                // linked ancestors (for example /var -> /private/var on macOS).
                current = Physical(target.FullName, remainingLinks - 1);
            }
        }
        return Path.GetFullPath(current);
    }
}
