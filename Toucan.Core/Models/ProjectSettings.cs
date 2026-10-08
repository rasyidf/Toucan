using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toucan.Core.Models;

/// <summary>
/// Per-project settings stored in toucan.tproj file.
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
    /// <summary>Format identifier (see <see cref="FormatIds"/>). Persisted as "saveFormat".</summary>
    public string SaveFormat { get; set; } = FormatIds.Json;

    /// <summary>
    /// Read-only migration hook for project files written before string format IDs ("saveStyle": enum number or name).
    /// Consumed by <see cref="LoadFrom"/> and never written back.
    /// </summary>
    [JsonPropertyName("saveStyle")]
    public JsonElement? LegacySaveStyle { get; set; }

    /// <summary>Built-in format as an enum. Returns <see cref="SaveStyles.Json"/> for plugin formats; prefer <see cref="SaveFormat"/>.</summary>
    [JsonIgnore]
    public SaveStyles SaveStyle
    {
        get => FormatIds.TryGetStyle(SaveFormat, out var style) ? style : SaveStyles.Json;
        set => SaveFormat = FormatIds.FromStyle(value);
    }
    /// <summary>Output encoding override: UTF-8 or UTF-8 BOM; null keeps the format default.</summary>
    public string? TextEncoding { get; set; }
    /// <summary>Output newline override: LF or CRLF; null keeps the format default.</summary>
    public string? LineEnding { get; set; }
    public string? Framework { get; set; }
    public List<TranslationPackage> TranslationPackages { get; set; } = [];

    // --- Editor preferences (project-scoped, null = inherit from ProjectDefaults) ---
    public bool? SaveEmptyTranslations { get; set; } = true;
    public string? TranslationOrder { get; set; } = "alphabetical";
    public List<string>? CopyTemplates { get; set; } = ["%1"];
    public bool? CommentsEnabled { get; set; } = true;

    /// <summary>
    /// When true, a translation with validation errors cannot be approved. Saving is never blocked by validation
    /// (drafts are always saveable); this is the strict policy for approval, and later delivery.
    /// </summary>
    public bool? RequireValidForApproval { get; set; }

    // --- Provider overrides (project-scoped) ---
    public string? DefaultProvider { get; set; }

    // --- Translation context ---
    public string? Context { get; set; }
    public string? Formality { get; set; }
    public bool? PreservePlaceholders { get; set; }
    public bool? PreviewBeforeApply { get; set; }

    /// <summary>Namespace prefixes hidden from the editor view and statistics.</summary>
    public List<string> HiddenNamespaces { get; set; } = [];

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
    public List<string>? SourceRoots { get; set; } = [];

    /// <summary>External editor command for "open in editor" (e.g., "code --goto {file}:{line}").</summary>
    public string? ExternalEditor { get; set; }

    /// <summary>File extensions to include in source code scanning.</summary>
    public List<string>? ScanExtensions { get; set; }

    /// <summary>Directories to exclude from source code scanning.</summary>
    public List<string>? ExcludedDirectories { get; set; }

    /// <summary>Automatically scan source code when opening the project.</summary>
    public bool? AutoScanOnOpen { get; set; }

    // --- Validation ---
    /// <summary>Run validation pipeline on save.</summary>
    public bool? ValidateOnSave { get; set; }

    /// <summary>Per-rule enable/severity overrides. Key = rule ID. Null = inherit from defaults.</summary>
    public Dictionary<string, Core.Options.ValidationRuleConfig>? ValidationRules { get; set; }

    // --- Auto-save configuration ---
    public bool? AutoSaveEnabled { get; set; }
    public int? AutoSaveIntervalSeconds { get; set; } = 60; // Clamped to 10-600

    // --- Runtime (not serialized) ---
    [JsonIgnore] public string ProjectPath { get; set; } = string.Empty;
    [JsonIgnore] public bool IsDirty { get; set; }

    public static ProjectSettings? LoadFrom(string folder)
    {
        var file = Path.Combine(folder, "toucan.tproj");
        if (!File.Exists(file)) return null;

        try
        {
            var json = File.ReadAllText(file);
            var settings = JsonSerializer.Deserialize<ProjectSettings>(json, s_options) ?? new ProjectSettings();
            settings.ProjectPath = folder;
            settings.MigrateLegacyFormat(json);
            return settings;
        }
        catch { return null; }
    }

    private void MigrateLegacyFormat(string json)
    {
        var legacy = LegacySaveStyle;
        LegacySaveStyle = null;
        if (legacy is not { } value) return;

        // A file that already has "saveFormat" wins over a stale "saveStyle".
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("saveFormat", out _)) return;

        if (FormatIds.FromLegacy(value) is { } id) SaveFormat = id;
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

        // ponytail: ensure translationPackages reflects Languages so ManifestLoadStrategy always works.
        // Backfill only the first package when it has no URLs (multi-package projects manage their own URLs).
        if (Languages.Count > 0)
        {
            if (TranslationPackages.Count == 0)
                TranslationPackages.Add(new TranslationPackage { Name = "main" });
            if (TranslationPackages[0].TranslationUrls.Count == 0)
            {
                TranslationPackages[0].TranslationUrls = Languages
                    .Select(lang => new TranslationUrl { Language = lang, Path = ResolveDefaultPath(lang) })
                    .ToList();
            }
        }

        var json = JsonSerializer.Serialize(this, s_options);
        Toucan.Core.Services.AtomicFile.WriteAllText(Path.Combine(ProjectPath, "toucan.tproj"), json);
        IsDirty = false;
    }

    /// <summary>
    /// Maps (format ID, language) to the default relative file path. Set by the project service so plugin formats
    /// resolve through their strategy; when null, <c>{language}.json</c> is used.
    /// </summary>
    [JsonIgnore] public Func<string, string, string>? DefaultPathResolver { get; set; }

    private string ResolveDefaultPath(string language) =>
        DefaultPathResolver?.Invoke(SaveFormat, language) ?? $"{language}.json";
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
