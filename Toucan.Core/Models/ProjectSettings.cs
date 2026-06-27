using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toucan.Core.Models;

/// <summary>
/// Per-project settings stored in toucan.project file.
/// Contains everything about this specific translation project.
/// </summary>
public class ProjectSettings
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // --- Project identity ---
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }

    // --- Language config ---
    public string PrimaryLanguage { get; set; } = "en-US";
    public List<string> Languages { get; set; } = [];

    // --- Format / IO ---
    public SaveStyles SaveStyle { get; set; } = SaveStyles.Json;
    public string? Framework { get; set; }
    public List<TranslationPackage> TranslationPackages { get; set; } = [];

    // --- Editor preferences (project-scoped) ---
    public bool SaveEmptyTranslations { get; set; } = true;
    public string TranslationOrder { get; set; } = "alphabetical";
    public List<string> CopyTemplates { get; set; } = ["%1"];

    // --- Provider overrides (project-scoped) ---
    public string? DefaultProvider { get; set; }

    // --- Language alias mapping (file-system code → display code) ---
    public Dictionary<string, string>? LanguageAliases { get; set; }

    // --- Per-language file locations (override default path conventions) ---
    /// <summary>
    /// Custom file paths per language. Key = language code, Value = relative path from project root.
    /// If a language is not listed here, the framework profile's default path is used.
    /// Example: { "fr-FR": "custom/translations/french.json" }
    /// </summary>
    public Dictionary<string, string>? LanguageFilePaths { get; set; }

    // --- Source code integration ---
    /// <summary>Relative paths to source code directories (for key usage scanning).</summary>
    public List<string> SourceRoots { get; set; } = [];

    /// <summary>External editor command for "open in editor" (e.g., "code --goto {file}:{line}").</summary>
    public string? ExternalEditor { get; set; }

    // --- Runtime (not serialized) ---
    [JsonIgnore] public string ProjectPath { get; set; } = string.Empty;
    [JsonIgnore] public bool IsDirty { get; set; }

    public static ProjectSettings? LoadFrom(string folder)
    {
        var file = Path.Combine(folder, "toucan.project");
        if (!File.Exists(file)) return null;

        try
        {
            var json = File.ReadAllText(file);
            var settings = JsonSerializer.Deserialize<ProjectSettings>(json, s_options) ?? new ProjectSettings();
            settings.ProjectPath = folder;
            return settings;
        }
        catch { return null; }
    }

    public static ProjectSettings CreateDefault(string folder, string? name = null)
    {
        return new ProjectSettings
        {
            Name = name ?? Path.GetFileName(folder),
            ProjectPath = folder,
            PrimaryLanguage = "en-US",
            Languages = ["en-US"]
        };
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(ProjectPath)) return;
        Directory.CreateDirectory(ProjectPath);
        var json = JsonSerializer.Serialize(this, s_options);
        File.WriteAllText(Path.Combine(ProjectPath, "toucan.project"), json);
        IsDirty = false;
    }
}

/// <summary>A named package of translation files within a project.</summary>
public class TranslationPackage
{
    public string Name { get; set; } = "main";
    public List<TranslationUrl> TranslationUrls { get; set; } = [];
}

public class TranslationUrl
{
    public string Language { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
