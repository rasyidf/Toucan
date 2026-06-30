using System;
using System.Collections.Generic;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Registry of known translation providers and their option/secret schemas.
/// Built-in definitions are hard-coded to match the actual provider implementations.
/// </summary>
public class TranslationProviderRegistry : ITranslationProviderRegistry
{
    private static readonly List<ProviderDefinition> s_builtIn =
    [
        new ProviderDefinition
        {
            Name = "Google",
            DisplayName = "Google Translate",
            Description = "Google Cloud Translation API v2",
            IsBuiltIn = true,
            OptionFields = [],
            SecretFields = new() { ["api_key"] = "Google Cloud API key" },
            DefaultValues = []
        },
        new ProviderDefinition
        {
            Name = "DeepL",
            DisplayName = "DeepL",
            Description = "DeepL Translator API (Free or Pro)",
            IsBuiltIn = true,
            OptionFields = new() { ["endpoint"] = "API endpoint URL" },
            SecretFields = new() { ["api_key"] = "DeepL API authentication key" },
            DefaultValues = new() { ["endpoint"] = "https://api.deepl.com/v2/translate" }
        },
        new ProviderDefinition
        {
            Name = "Microsoft",
            DisplayName = "Microsoft Translator",
            Description = "Azure Cognitive Services Translator",
            IsBuiltIn = true,
            OptionFields = new()
            {
                ["endpoint"] = "Translator endpoint URL",
                ["region"] = "Azure region (e.g. eastus)"
            },
            SecretFields = new() { ["api_key"] = "Azure subscription key" },
            DefaultValues = new()
            {
                ["endpoint"] = "https://api.cognitive.microsofttranslator.com",
                ["region"] = ""
            }
        },
        new ProviderDefinition
        {
            Name = "OpenAI",
            DisplayName = "OpenAI / Compatible",
            Description = "OpenAI, Azure OpenAI, Ollama, or any compatible endpoint",
            IsBuiltIn = true,
            OptionFields = new()
            {
                ["endpoint"] = "API base URL",
                ["model"] = "Model name",
                ["prompt"] = "Custom system prompt (optional)"
            },
            SecretFields = new() { ["api_key"] = "API key / Bearer token" },
            DefaultValues = new()
            {
                ["endpoint"] = "https://api.openai.com/v1",
                ["model"] = "gpt-4o-mini",
                ["prompt"] = ""
            }
        },
        new ProviderDefinition
        {
            Name = "Custom",
            DisplayName = "Custom Webhook",
            Description = "POST to a user-defined translation endpoint",
            IsBuiltIn = true,
            OptionFields = new()
            {
                ["endpoint"] = "Webhook URL (required)",
                ["header_name"] = "Auth header name (default: Authorization)"
            },
            SecretFields = new() { ["api_key"] = "Bearer token (optional)" },
            DefaultValues = new()
            {
                ["endpoint"] = "",
                ["header_name"] = ""
            }
        }
    ];

    public IReadOnlyList<ProviderDefinition> GetAll() => s_builtIn;

    public ProviderDefinition? GetByName(string name)
        => s_builtIn.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
}
