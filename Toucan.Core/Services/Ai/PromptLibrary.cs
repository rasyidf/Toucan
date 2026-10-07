using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Core.Services.Ai;

/// <summary>Prompt files: <c>&lt;user folder&gt;/&lt;feature&gt;.md</c> and <c>&lt;project&gt;/.toucan/prompts/&lt;feature&gt;.md</c>.</summary>
public sealed class PromptLibrary(IEnumerable<AiFeatureDefinition> features, string userFolder) : IPromptLibrary
{
    private readonly IReadOnlyList<AiFeatureDefinition> _features = [.. features.DistinctBy(f => f.Id, StringComparer.OrdinalIgnoreCase)];

    public static string DefaultUserFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "prompts");

    public IReadOnlyList<AiFeatureDefinition> Features => _features;

    public string UserFolder => userFolder;

    public string ProjectFolder(string projectPath) => Path.Combine(projectPath, ".toucan", "prompts");

    public AiFeatureDefinition? GetFeature(string featureId) =>
        _features.FirstOrDefault(f => string.Equals(f.Id, featureId, StringComparison.OrdinalIgnoreCase));

    public AiPrompt GetPrompt(string featureId, string? projectPath = null)
    {
        var feature = Require(featureId);
        return (string.IsNullOrEmpty(projectPath) ? null : GetOverride(feature.Id, PromptSource.Project, projectPath))
            ?? GetOverride(feature.Id, PromptSource.User)
            ?? new AiPrompt(feature.Id, feature.DefaultPrompt, PromptSource.BuiltIn, null);
    }

    public AiPrompt? GetOverride(string featureId, PromptSource scope, string? projectPath = null)
    {
        var feature = Require(featureId);
        var path = PathFor(feature.Id, scope, projectPath);
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var text = File.ReadAllText(path).ReplaceLineEndings("\n").Trim();
            return text.Length == 0 ? null : new AiPrompt(feature.Id, text, scope, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string featureId, string text, PromptSource scope, string? projectPath = null)
    {
        var feature = Require(featureId);
        var normalized = (text ?? string.Empty).ReplaceLineEndings("\n").Trim();
        // Saving the user's version as the default (or empty) means "use the default"; a project version is kept as typed,
        // so a project can pin the default even when the user's own version differs.
        if (normalized.Length == 0 || (scope == PromptSource.User && normalized == feature.DefaultPrompt))
        {
            Reset(feature.Id, scope, projectPath);
            return;
        }

        var path = PathFor(feature.Id, scope, projectPath) ?? throw new ArgumentException("Only user and project prompts can be saved.", nameof(scope));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, normalized + "\n");
    }

    public void Reset(string featureId, PromptSource scope, string? projectPath = null)
    {
        var path = PathFor(Require(featureId).Id, scope, projectPath);
        if (path != null && File.Exists(path)) File.Delete(path);
    }

    private string? PathFor(string featureId, PromptSource scope, string? projectPath) => scope switch
    {
        PromptSource.User => Path.Combine(userFolder, featureId + ".md"),
        PromptSource.Project when !string.IsNullOrEmpty(projectPath) => Path.Combine(ProjectFolder(projectPath), featureId + ".md"),
        _ => null,
    };

    private AiFeatureDefinition Require(string featureId) =>
        GetFeature(featureId) ?? throw new ArgumentException($"Unknown AI feature '{featureId}'.", nameof(featureId));
}
