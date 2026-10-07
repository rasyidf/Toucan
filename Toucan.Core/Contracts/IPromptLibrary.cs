using Toucan.Core.Models;

namespace Toucan.Core.Contracts;

/// <summary>Where a prompt's text came from. A project's version wins over the user's, which wins over the built-in one.</summary>
public enum PromptSource { BuiltIn, User, Project }

/// <summary>The prompt a feature will use, and the file it was read from (null for the built-in default).</summary>
public sealed record AiPrompt(string FeatureId, string Text, PromptSource Source, string? FilePath);

/// <summary>
/// The system prompts of the AI features. Every prompt is plain text and editable: the defaults ship in Toucan's source
/// (<c>Toucan.Core/Ai/Prompts</c>), the user's versions live in <c>Documents/Toucan/prompts/&lt;feature&gt;.md</c>, and a
/// project can carry its own in <c>.toucan/prompts/&lt;feature&gt;.md</c> (no secrets, so safe to commit).
/// </summary>
public interface IPromptLibrary
{
    IReadOnlyList<AiFeatureDefinition> Features { get; }

    AiFeatureDefinition? GetFeature(string featureId);

    /// <summary>The prompt in effect: the project's, else the user's, else the built-in default.</summary>
    AiPrompt GetPrompt(string featureId, string? projectPath = null);

    /// <summary>The version saved at exactly <paramref name="scope"/>, or null when there is none.</summary>
    AiPrompt? GetOverride(string featureId, PromptSource scope, string? projectPath = null);

    /// <summary>Saves a user or project version. Text equal to the built-in default removes the override instead.</summary>
    void Save(string featureId, string text, PromptSource scope, string? projectPath = null);

    /// <summary>Removes the user or project version, so the next level down is used again.</summary>
    void Reset(string featureId, PromptSource scope, string? projectPath = null);

    /// <summary>Folder of the user's prompt files.</summary>
    string UserFolder { get; }

    /// <summary>Folder of a project's prompt files.</summary>
    string ProjectFolder(string projectPath);
}
