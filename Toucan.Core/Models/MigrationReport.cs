using Toucan.Core.Contracts.Services;
using Toucan.Core.Services;

namespace Toucan.Core.Models;

/// <summary>Reopenable migration summary. No translation values or original source payloads.</summary>
public sealed class MigrationReport
{
    public string Source { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string NativeManifest { get; set; } = "toucan.tproj";
    public string Framework { get; set; } = "";
    public List<ResourceDescriptor> Resources { get; set; } = [];
    public ResourceOptions ProjectOptions { get; set; } = new();
    public List<ResourceDiagnostic> Diagnostics { get; set; } = [];
    public Dictionary<string, string> InputHashes { get; set; } = [];
    public bool InputsUnchanged(string root) => InputHashes.All(p => ResourceConfigurationPlanner.Fingerprint(ResourceFilePath.Resolve(root, p.Key)) == p.Value);
    public static MigrationReport Inspect(string file, IProjectService service)
    {
        var first = service.LoadProject(file);
        var root = first.Settings.ProjectPath;
        var inputs = first.Settings.ResourceDocuments.Select(d => d.Resource.RelativePath).Append(Path.GetFileName(file)).Distinct()
            .ToDictionary(p => p, p => ResourceConfigurationPlanner.Fingerprint(ResourceFilePath.Resolve(root, p)));
        var loaded = service.LoadProject(file);
        var report = new MigrationReport { Source = Path.GetFileName(file), CreatedAtUtc = DateTime.UtcNow,
            Framework = loaded.Settings.Framework ?? "", Resources = loaded.Settings.ResourceDocuments.Select(d => d.Resource).ToList(),
            ProjectOptions = loaded.Settings.ResourceOptions, Diagnostics = loaded.Settings.ImportDiagnostics.ToList(), InputHashes = inputs };
        if (!report.InputsUnchanged(root)) throw new IOException("Migration inputs changed during preview. Retry the import.");
        return report;
    }
    public string Summary => $"{Source} → {NativeManifest}\n{Framework}\n" +
        string.Join("\n", Resources.Select(r => $"{r.RelativePath} | {r.Package} | {r.Language} | {r.FormatId}/{r.Dialect} | {r.Ownership}")) +
        "\n\n" + System.Text.Json.JsonSerializer.Serialize(ProjectOptions) + "\n\n" +
        string.Join("\n", Diagnostics.Select(d => $"{d.Severity} [{d.Code}] {d.RelativePath} / {d.Field}: {d.Message}"));
}
