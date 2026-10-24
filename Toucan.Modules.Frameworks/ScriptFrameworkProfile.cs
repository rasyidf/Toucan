using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Services.Frameworks;

public static class FrameworkFeatureSettings
{
    public static ConfigSchema Schema { get; } = new() { Fields = [
        new() { Key = "defaultPackage", Label = "Default source package", Description = "Package for source keys without an explicit namespace.", Default = "main", Required = true, Scope = ConfigScope.Workspace },
        new() { Key = "resolveNamespaces", Label = "Resolve source namespaces", Description = "Use explicit namespaces and scoped bindings when matching source references.", Type = ConfigFieldType.Boolean, Default = true, Scope = ConfigScope.Workspace },
        new() { Key = "keyPrefix", Label = "Source key prefix", Description = "Prefix for static relative keys. Leave empty when keys are already complete.", Default = "", Scope = ConfigScope.Workspace }
    ] };
}

/// <summary>Discovery conventions are intentionally small; exact resource descriptors remain user-reviewable.</summary>
public sealed class ScriptFrameworkProfile(string id, string displayName, string scannerId) : IFrameworkProfile, IFrameworkFeatures
{
    public string Id => id;
    public string DisplayName => displayName;
    public string DefaultFormatId => id == "formatjs" ? "formatjs" : FormatIds.Json;
    public string ScannerId => scannerId;
    public ConfigSchema Settings => FrameworkFeatureSettings.Schema;
    public IEnumerable<string> FilePatterns => id == "formatjs" ? ["extracted/*.json", "locales/*.json"] : ["locales/*.json"];
    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder)
    {
        foreach (var dir in id == "formatjs" ? new[] { "extracted", "locales" } : ["locales"])
            if (Directory.Exists(Path.Combine(rootFolder, dir)))
                foreach (var file in Directory.EnumerateFiles(Path.Combine(rootFolder, dir), "*.json"))
                    yield return new(file, Path.GetRelativePath(rootFolder, file), Path.GetFileNameWithoutExtension(file), "main");
    }
    public ResourceDescriptor Describe(DiscoveredFile file)
    {
        var source = id == "formatjs" && file.RelativePath.Replace('\\', '/').StartsWith("extracted/", StringComparison.Ordinal);
        return new() { Id = file.RelativePath.Replace('\\', '/'), RelativePath = file.RelativePath.Replace('\\', '/'), Language = file.Language,
            Package = file.Package ?? "main", FormatId = source ? "formatjs" : FormatIds.Json, Dialect = source ? "formatjs-extract" : id == "formatjs" ? "formatjs-target" : null,
            Ownership = source ? ResourceOwnership.GeneratedSource : ResourceOwnership.User,
            Options = new() { Layout = ResourceLayout.Literal } };
    }
    public string? ExtractLanguage(string relativePath) => Path.GetFileNameWithoutExtension(relativePath);
    public string GetFilePath(string rootFolder, string language, string? package = null) => Path.Combine(rootFolder, "locales", language + ".json");
    public int DetectionScore(string rootFolder) => id == "formatjs" && Directory.Exists(Path.Combine(rootFolder, "extracted")) ? 80 : 0;
}
