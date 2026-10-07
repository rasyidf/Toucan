using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Core.Services.Ai;

/// <summary>
/// AI Integration. Every AI request goes through <see cref="CompleteAsync"/>, which checks the app-wide switch and the
/// feature's own switch, picks the service, model and key, and renders the feature's prompt (project, user or built-in).
/// </summary>
/// <remarks>
/// For CI, setting <c>TOUCAN_AI_BACKEND</c> (e.g. <c>anthropic</c>) turns AI on for that process with that service,
/// without touching the saved settings; the key then comes from the secret store or the service's environment variable.
/// </remarks>
public sealed class AiService(IEnumerable<IAiBackend> backends, IAiSettingsStore store, ISecretService secrets, IPromptLibrary prompts) : IAiService
{
    public const string BackendEnvironmentVariable = "TOUCAN_AI_BACKEND";

    private readonly IReadOnlyList<IAiBackend> _backends = [.. backends];

    public IReadOnlyList<AiBackendDefinition> Backends => [.. _backends.Select(b => b.Definition)];

    public bool IsEnabled => Effective().Enabled;

    public bool IsFeatureEnabled(string featureId)
    {
        var settings = Effective();
        return settings.Enabled && prompts.GetFeature(featureId) != null && settings.FeatureSettings(featureId).Enabled;
    }

    public AiStatus GetStatus()
    {
        var settings = Effective();
        var backend = Find(settings.Backend);
        if (backend == null)
            return new AiStatus(settings.Enabled, settings.Backend, settings.Backend, string.Empty, false, $"The AI service '{settings.Backend}' is not installed.");

        var def = backend.Definition;
        var endpoint = Resolve(settings, backend, null);
        var hasKey = !string.IsNullOrEmpty(endpoint.ApiKey);
        string? problem = !settings.Enabled ? "AI is turned off."
            : def.RequiresApiKey && !hasKey ? $"{def.DisplayName} has no API key. Add it under Settings → AI."
            : null;
        return new AiStatus(settings.Enabled, def.Id, def.DisplayName, endpoint.Model, hasKey, problem);
    }

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = Effective();
        if (!settings.Enabled) throw new AiUnavailableException("AI is turned off. Turn it on under Settings → AI.");

        var feature = prompts.GetFeature(request.FeatureId) ?? throw new AiUnavailableException($"Unknown AI feature '{request.FeatureId}'.");
        var featureSettings = settings.FeatureSettings(feature.Id);
        if (!featureSettings.Enabled) throw new AiUnavailableException($"The AI feature \"{feature.DisplayName}\" is turned off under Settings → AI.");

        var backend = Find(settings.Backend) ?? throw new AiUnavailableException($"The AI service '{settings.Backend}' is not installed.");
        var endpoint = Resolve(settings, backend, featureSettings.Model);
        if (backend.Definition.RequiresApiKey && string.IsNullOrEmpty(endpoint.ApiKey))
            throw new AiUnavailableException($"{backend.Definition.DisplayName} has no API key. Add it under Settings → AI.");

        // Every variable the feature declares is known to the template, empty when the caller has no value for it,
        // so its sections are dropped instead of being sent as literal {{#name}} text.
        var variables = feature.Variables.ToDictionary(v => v.Name, string? (_) => null, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in request.Variables) variables[name] = value;
        var system = PromptTemplate.Render(prompts.GetPrompt(feature.Id, request.ProjectPath).Text, variables);
        var completion = new AiCompletionRequest
        {
            System = system,
            User = request.Input,
            Temperature = feature.Temperature,
            JsonResponse = feature.JsonResponse,
        };
        return await backend.CompleteAsync(completion, endpoint, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The endpoint, model and key a request would use with these settings. The key comes from the secret store, else the service's environment variable.</summary>
    public AiEndpoint Resolve(AiSettings settings, IAiBackend backend, string? modelOverride)
    {
        var def = backend.Definition;
        var saved = settings.BackendSettings(def.Id);
        var key = secrets.GetSecret(SecretKeys.Ai(def.Id))
            ?? (def.ApiKeyEnvironmentVariable is { } env ? Environment.GetEnvironmentVariable(env) : null);
        return new AiEndpoint(
            string.IsNullOrWhiteSpace(saved.Endpoint) ? def.DefaultEndpoint : saved.Endpoint.Trim(),
            !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride.Trim() : string.IsNullOrWhiteSpace(saved.Model) ? def.DefaultModel : saved.Model.Trim(),
            string.IsNullOrWhiteSpace(key) ? null : key.Trim());
    }

    public IAiBackend? Find(string id) =>
        _backends.FirstOrDefault(b => string.Equals(b.Definition.Id, id, StringComparison.OrdinalIgnoreCase));

    private AiSettings Effective()
    {
        var settings = store.Load();
        if (Environment.GetEnvironmentVariable(BackendEnvironmentVariable) is { Length: > 0 } forced)
        {
            settings.Enabled = true;
            settings.Backend = forced.Trim();
        }
        return settings;
    }
}
