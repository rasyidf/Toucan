using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toucan.Plugins;

/// <summary>Contents of a plugin's <c>plugin.json</c>.</summary>
public sealed record PluginManifest
{
    public const string FileName = "plugin.json";

    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Unique, stable identifier: lowercase letters, digits, '.', '-' (e.g. <c>acme.po-plus</c>).</summary>
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;

    /// <summary>Plugin API version the plugin was built against, e.g. <c>1.0</c>.</summary>
    public string ApiVersion { get; init; } = string.Empty;

    /// <summary>File name of the assembly containing the <see cref="IToucanPlugin"/>, relative to the plugin folder.</summary>
    public string EntryAssembly { get; init; } = string.Empty;

    /// <summary>
    /// Full name of the <see cref="IToucanPlugin"/> class. Optional: when omitted the assembly must contain exactly
    /// one public implementation.
    /// </summary>
    public string? EntryType { get; init; }
    public string? Author { get; init; }

    /// <summary>
    /// The plugin's desktop part: a second assembly with its UI, loaded only by hosts that have one. Needs the
    /// <c>desktop</c> capability. The CLI never loads it.
    /// </summary>
    public DesktopEntry? Desktop { get; init; }
    public string? Description { get; init; }

    /// <summary>What the plugin registers; the host rejects registrations outside this set (see <see cref="PluginCapabilities"/>).</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Parses and validates a manifest. Returns false with human-readable errors instead of throwing.</summary>
    public static bool TryParse(string json, out PluginManifest manifest, out IReadOnlyList<string> errors)
    {
        manifest = new PluginManifest();
        var problems = new List<string>();
        errors = problems;

        PluginManifest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PluginManifest>(json, s_options);
        }
        catch (JsonException ex)
        {
            problems.Add($"{FileName} is not valid JSON: {ex.Message}");
            return false;
        }

        if (parsed is null)
        {
            problems.Add($"{FileName} is empty.");
            return false;
        }

        manifest = parsed;
        problems.AddRange(parsed.Validate());
        return problems.Count == 0;
    }

    /// <summary>The manifest's <see cref="ApiVersion"/> as a <see cref="System.Version"/>, or null if malformed.</summary>
    [JsonIgnore]
    public Version? ParsedApiVersion => System.Version.TryParse(ApiVersion, out var v) ? v : null;

    private IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) yield return "'id' is required.";
        else if (!IsValidId(Id)) yield return $"'id' must use lowercase letters, digits, '.' and '-' only: '{Id}'.";

        if (string.IsNullOrWhiteSpace(Name)) yield return "'name' is required.";
        if (string.IsNullOrWhiteSpace(Version) || !System.Version.TryParse(Version, out _)) yield return "'version' is required and must look like 1.2.3.";
        if (ParsedApiVersion is null) yield return "'apiVersion' is required and must look like 1.0.";

        if (string.IsNullOrWhiteSpace(EntryAssembly)) yield return "'entryAssembly' is required.";
        else if (EntryAssembly.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(EntryAssembly)
                 || EntryAssembly.AsSpan().IndexOfAny('/', '\\') >= 0)
            yield return "'entryAssembly' must be a file name inside the plugin folder.";
        else if (!EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) yield return "'entryAssembly' must end with .dll.";

        if (Desktop is not null)
        {
            foreach (var problem in Desktop.Validate()) yield return $"'desktop': {problem}";
            if (!(Capabilities ?? []).Contains(PluginCapabilities.Desktop, StringComparer.OrdinalIgnoreCase))
                yield return $"'desktop' needs the '{PluginCapabilities.Desktop}' capability.";
        }

        foreach (var capability in (Capabilities ?? []).Except(PluginCapabilities.All, StringComparer.OrdinalIgnoreCase))
            yield return $"Unknown capability '{capability}'. Known: {string.Join(", ", PluginCapabilities.All)}.";
    }

    private static bool IsValidId(string id) =>
        id.Length <= 64 && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-')
        && char.IsLetterOrDigit(id[0]) && char.IsLetterOrDigit(id[^1]);
}

/// <summary>The <c>desktop</c> object of a manifest.</summary>
public sealed record DesktopEntry
{
    /// <summary>File name of the assembly containing the <c>IToucanDesktopPlugin</c>, relative to the plugin folder.</summary>
    public string EntryAssembly { get; init; } = string.Empty;

    /// <summary>Full name of the <c>IToucanDesktopPlugin</c> class; optional when the assembly has exactly one.</summary>
    public string? EntryType { get; init; }

    /// <summary>Desktop contract version the assembly was built against, e.g. <c>1.0</c>.</summary>
    public string ContractVersion { get; init; } = string.Empty;

    [JsonIgnore]
    public Version? ParsedContractVersion => Version.TryParse(ContractVersion, out var v) ? v : null;

    internal IEnumerable<string> Validate()
    {
        if (string.IsNullOrWhiteSpace(EntryAssembly)) yield return "'entryAssembly' is required.";
        else if (EntryAssembly.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(EntryAssembly)
                 || EntryAssembly.AsSpan().IndexOfAny('/', '\\') >= 0)
            yield return "'entryAssembly' must be a file name inside the plugin folder.";
        else if (!EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) yield return "'entryAssembly' must end with .dll.";

        if (ParsedContractVersion is null) yield return "'contractVersion' is required and must look like 1.0.";
    }
}

/// <summary>Capabilities a plugin may declare in its manifest.</summary>
public static class PluginCapabilities
{
    public const string Formats = "formats";
    public const string Providers = "providers";
    public const string Validation = "validation";
    public const string Frameworks = "frameworks";
    public const string Activation = "activation";
    public const string Commands = "commands";
    public const string Desktop = "desktop";

    public static IReadOnlyList<string> All { get; } = [Formats, Providers, Validation, Frameworks, Activation, Commands, Desktop];
}
