using System.Text.Json;
using System.Text.Json.Nodes;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Core.Services.Ai;

/// <summary>
/// Up to 0.19, Claude, OpenAI and Gemini were translation providers configured in <c>providers.json</c>. They are AI
/// services now. This moves their endpoint and model into <see cref="AiSettings"/>, their API keys into the secret store,
/// and a custom prompt into the user's Translate prompt, then removes the entries from <c>providers.json</c>.
/// AI stays off: the user turns it on in onboarding or under Settings → AI.
/// </summary>
public static class LegacyAiMigration
{
    /// <summary>Old provider name → AI service id.</summary>
    public static IReadOnlyDictionary<string, string> LegacyProviders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Claude"] = "anthropic",
        ["OpenAI"] = "openai",
        ["Gemini"] = "gemini",
    };

    /// <summary>"AI" for a provider name saved before 0.20 (Claude, OpenAI, Gemini), otherwise the name unchanged.</summary>
    public static string? CurrentProviderName(string? name) =>
        name != null && LegacyProviders.ContainsKey(name) ? AiFeatureIds.TranslationProviderName : name;

    private static readonly Dictionary<string, (string Endpoint, string Model)> s_oldDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["anthropic"] = ("https://api.anthropic.com", "claude-haiku-4-5-20251001"),
        ["openai"] = ("https://api.openai.com/v1", "gpt-4o-mini"),
        ["gemini"] = ("https://generativelanguage.googleapis.com", "gemini-flash-latest"),
    };

    /// <param name="providersFile">The app-wide <c>providers.json</c>.</param>
    /// <param name="lastProvider">The provider last used for machine translation; picks the AI service when it was one of the three.</param>
    /// <param name="translatePromptFile">Where the user's Translate prompt goes if an old provider had a custom prompt.</param>
    public static AiSettings Migrate(string providersFile, ISecureStorageService protector, ISecretService secrets, string? lastProvider, string translatePromptFile)
    {
        var settings = new AiSettings();
        if (!File.Exists(providersFile)) return settings;

        JsonArray? entries;
        try { entries = JsonNode.Parse(File.ReadAllText(providersFile)) as JsonArray; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return settings; }
        if (entries == null) return settings;

        var withKey = new List<string>();
        string? customPrompt = null;
        foreach (var entry in entries.OfType<JsonObject>().ToList())
        {
            var name = Read(entry, "Provider");
            if (name == null || !LegacyProviders.TryGetValue(name, out var id)) continue;

            var options = Section(entry, "Options");
            var (defaultEndpoint, defaultModel) = s_oldDefaults[id];
            var endpoint = Read(options, "endpoint");
            var model = Read(options, "model");
            settings.Backends[id] = new AiBackendSettings
            {
                Endpoint = endpoint == defaultEndpoint ? null : endpoint,
                Model = model == defaultModel ? null : model,
            };
            customPrompt ??= Read(options, "prompt");

            var cipher = Read(Section(entry, "Secrets"), "api_key");
            var key = cipher == null ? null : protector.Unprotect(cipher);
            if (!string.IsNullOrEmpty(key))
            {
                secrets.SetSecret(SecretKeys.Ai(id), key);
                withKey.Add(id);
            }

            entries.Remove(entry);
        }

        if (settings.Backends.Count == 0) return settings;

        settings.Backend = lastProvider != null && LegacyProviders.TryGetValue(lastProvider, out var last) && settings.Backends.ContainsKey(last)
            ? last
            : withKey.FirstOrDefault() ?? settings.Backends.Keys.First();

        if (!string.IsNullOrWhiteSpace(customPrompt) && !File.Exists(translatePromptFile))
        {
            // The old custom prompt replaced the instructions and Toucan appended the context; keep doing that.
            Directory.CreateDirectory(Path.GetDirectoryName(translatePromptFile)!);
            File.WriteAllText(translatePromptFile, customPrompt.Trim() + "\n{{#context}}\nApplication context: {{context}}\n{{/context}}");
        }

        File.WriteAllText(providersFile, entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return settings;
    }

    private static JsonObject? Section(JsonObject entry, string name) =>
        entry.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;

    private static string? Read(JsonObject? obj, string name)
    {
        var node = obj?.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        return node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
    }
}
