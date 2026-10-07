namespace Toucan.Core.Options;

/// <summary>
/// AI Integration settings, stored in <c>Documents/Toucan/ai.json</c>. API keys are not in here: they are in the
/// secret store (<see cref="Contracts.ISecretService"/>, key <c>ai/&lt;service&gt;/api_key</c>). Prompts are not in here
/// either: they are plain files (see <see cref="Contracts.IPromptLibrary"/>).
/// </summary>
public sealed class AiSettings
{
    /// <summary>The app-wide switch. Off by default: no text is sent to an AI service until the user turns it on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Id of the AI service in use (<c>anthropic</c>, <c>openai</c>, <c>gemini</c>, or one a plugin adds).</summary>
    public string Backend { get; set; } = "anthropic";

    /// <summary>Endpoint and model per AI service, so switching services keeps each one's settings.</summary>
    public Dictionary<string, AiBackendSettings> Backends { get; set; } = [];

    /// <summary>Per-feature switches and model overrides, keyed by feature id (<c>translate</c>, <c>analyze</c>, <c>clarity</c>).</summary>
    public Dictionary<string, AiFeatureSettings> Features { get; set; } = [];

    public AiBackendSettings BackendSettings(string id) =>
        Backends.FirstOrDefault(kv => string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase)).Value ?? new AiBackendSettings();

    public AiFeatureSettings FeatureSettings(string id) =>
        Features.FirstOrDefault(kv => string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase)).Value ?? new AiFeatureSettings();

    public AiSettings Clone() => new()
    {
        Enabled = Enabled,
        Backend = Backend,
        Backends = Backends.ToDictionary(kv => kv.Key, kv => new AiBackendSettings { Endpoint = kv.Value.Endpoint, Model = kv.Value.Model }),
        Features = Features.ToDictionary(kv => kv.Key, kv => new AiFeatureSettings { Enabled = kv.Value.Enabled, Model = kv.Value.Model }),
    };
}

public sealed class AiBackendSettings
{
    /// <summary>API base URL; empty uses the service's default.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Model name; empty uses the service's default.</summary>
    public string? Model { get; set; }
}

public sealed class AiFeatureSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>A model for this feature only, e.g. a larger one for Analyze; empty uses the service's model.</summary>
    public string? Model { get; set; }
}
