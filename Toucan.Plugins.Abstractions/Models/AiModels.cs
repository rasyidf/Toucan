using System.Collections.Generic;

namespace Toucan.Core.Models;

/// <summary>Ids of the AI features that ship with Toucan. Each one has an editable system prompt (see docs/ai-integration.md).</summary>
public static class AiFeatureIds
{
    /// <summary>Machine translation through AI Integration (the "AI" translation provider).</summary>
    public const string Translate = "translate";

    /// <summary>Reviews translations against the source for terminology, tone and placeholder problems.</summary>
    public const string Analyze = "analyze";

    /// <summary>Reviews source-language strings for ambiguity and missing context before they are translated.</summary>
    public const string Clarity = "clarity";

    /// <summary>Name of the translation provider that routes machine translation through AI Integration.</summary>
    public const string TranslationProviderName = "AI";
}

/// <summary>Identity and defaults of an AI service (Anthropic, OpenAI-compatible, Gemini, or one a plugin adds).</summary>
public sealed class AiBackendDefinition
{
    /// <summary>Stable id saved in settings, e.g. <c>anthropic</c>.</summary>
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;
    public required string DefaultEndpoint { get; init; }
    public required string DefaultModel { get; init; }

    /// <summary>Environment variable read when no API key is saved, e.g. <c>ANTHROPIC_API_KEY</c>.</summary>
    public string? ApiKeyEnvironmentVariable { get; init; }

    /// <summary>False for local servers (Ollama, LM Studio) that accept requests without a key.</summary>
    public bool RequiresApiKey { get; init; } = true;
}

/// <summary>Where one request goes: the resolved endpoint, model and key.</summary>
public sealed record AiEndpoint(string Endpoint, string Model, string? ApiKey);

/// <summary>One chat completion: a system prompt and a single user message.</summary>
public sealed class AiCompletionRequest
{
    public required string System { get; init; }
    public required string User { get; init; }
    public double Temperature { get; init; } = 0.3;
    public int MaxTokens { get; init; } = 4096;

    /// <summary>Ask the service for JSON output where it supports a JSON mode.</summary>
    public bool JsonResponse { get; init; }
}

/// <summary>A request for an AI feature: the feature's prompt is rendered with <see cref="Variables"/> and sent with <see cref="Input"/>.</summary>
public sealed class AiRequest
{
    public required string FeatureId { get; init; }
    public required string Input { get; init; }
    public IReadOnlyDictionary<string, string?> Variables { get; init; } = new Dictionary<string, string?>();

    /// <summary>Open project folder, so a project's own prompt (<c>.toucan/prompts/&lt;id&gt;.md</c>) is used when it has one.</summary>
    public string? ProjectPath { get; init; }
}

/// <summary>A variable a feature's prompt can use as <c>{{name}}</c>, or as a section <c>{{#name}}…{{/name}}</c> kept only when it has a value.</summary>
public sealed record AiPromptVariable(string Name, string Description);

/// <summary>
/// An AI feature with an editable system prompt. Toucan registers Translate, Analyze and Clarity; a plugin can register
/// its own, and it then appears under Settings → AI with an editable prompt like the built-in ones.
/// </summary>
public sealed class AiFeatureDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;

    /// <summary>The prompt used when neither the user nor the project has their own version.</summary>
    public required string DefaultPrompt { get; init; }

    public IReadOnlyList<AiPromptVariable> Variables { get; init; } = [];

    /// <summary>What the reply must look like for Toucan to read it, shown next to the prompt editor.</summary>
    public string ResponseFormat { get; init; } = string.Empty;

    public double Temperature { get; init; } = 0.3;
    public bool JsonResponse { get; init; }
}

/// <summary>Whether AI can run right now, and why not.</summary>
public sealed record AiStatus(bool Enabled, string BackendId, string BackendName, string Model, bool HasApiKey, string? Problem)
{
    public bool IsReady => Enabled && Problem is null;
}
