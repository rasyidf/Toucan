using System.Text.Json;
using System.Text.Json.Serialization;
namespace Toucan.Core.Models;

public enum ResourceOwnership { User, GeneratedSource }

public sealed record ResourcePermissions(bool EditKeys = true, bool EditValues = true, bool EditMetadata = true, string? Reason = null)
{
    public static readonly ResourcePermissions Editable = new();
    public static readonly ResourcePermissions Generated = new(false, false, false, "This catalog is generated from source. Edit the source and extract it again.");
}

public sealed record FrameworkOptions
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; init; }
    public string DefaultPackage { get; init; } = "main";
    public bool ResolveNamespaces { get; init; } = true;
    public string KeyPrefix { get; init; } = "";
}
