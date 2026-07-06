using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Toucan.Core.Options;

/// <summary>
/// Default template for new projects. Provides fallback values when a project
/// doesn't override a field (null in toucan.tproj).
/// Stored in ~/Documents/Toucan/project-defaults.json.
/// </summary>
public class ProjectDefaults
{
    private static readonly string s_dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan");

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    // --- Editor defaults ---
    public string DefaultLanguage { get; set; } = "en-US";
    public bool SaveEmptyTranslations { get; set; } = true;
    public string TranslationOrder { get; set; } = "alphabetical";
    public List<string> CopyTemplates { get; set; } = ["%1", "{ this.props.t('%1') }", "{ t('%1') }"];
    public bool CommentsEnabled { get; set; } = true;

    // --- Translation defaults ---
    public string DefaultProvider { get; set; } = "Google";
    public string Formality { get; set; } = "Default";
    public string Context { get; set; } = "";
    public bool PreservePlaceholders { get; set; } = true;
    public bool PreviewBeforeApply { get; set; } = true;

    // --- Validation defaults ---
    public bool ValidateOnSave { get; set; } = true;
    public Dictionary<string, ValidationRuleConfig> ValidationRules { get; set; } = new()
    {
        ["missing-translation"] = new(true, "Warning"),
        ["placeholder-mismatch"] = new(true, "Error"),
        ["duplicate-key"] = new(true, "Error"),
        ["untranslated-copy"] = new(true, "Info"),
        ["empty-value"] = new(true, "Warning"),
        ["whitespace-mismatch"] = new(true, "Info"),
    };

    // --- Source Code defaults ---
    public List<string> SourceRoots { get; set; } = [];
    public string ExternalEditor { get; set; } = "code --goto {file}:{line}";
    public List<string> ScanExtensions { get; set; } = [".ts", ".tsx", ".js", ".jsx", ".vue", ".svelte", ".py", ".cs", ".kt", ".java", ".swift", ".dart", ".rb", ".php", ".go"];
    public List<string> ExcludedDirectories { get; set; } = ["node_modules", ".git", "dist", "build", "out", ".next", "__pycache__", "bin", "obj"];
    public bool AutoScanOnOpen { get; set; }
    public string LocaleFolderPattern { get; set; } = "locales";
    public string KeyMatcherPattern { get; set; } = "";

    // --- Feature defaults ---
    public bool AutoSaveEnabled { get; set; }
    public int AutoSaveIntervalSeconds { get; set; } = 60;

    // --- Languages ---
    public List<string> DefaultProjectLanguages { get; set; } = ["en-US"];

    public static ProjectDefaults LoadFromDisk()
    {
        var file = Path.Combine(s_dir, "project-defaults.json");
        if (!File.Exists(file)) return new ProjectDefaults();

        try
        {
            return JsonSerializer.Deserialize<ProjectDefaults>(File.ReadAllText(file), s_options) ?? new ProjectDefaults();
        }
        catch { return new ProjectDefaults(); }
    }

    public void ToDisk()
    {
        Directory.CreateDirectory(s_dir);
        File.WriteAllText(Path.Combine(s_dir, "project-defaults.json"), JsonSerializer.Serialize(this, s_options));
    }

    /// <summary>
    /// Seeds initial defaults from existing AppOptions on first run (migration).
    /// </summary>
    public static ProjectDefaults SeedFrom(AppOptions appOptions)
    {
        return new ProjectDefaults
        {
            DefaultLanguage = appOptions.DefaultLanguage ?? "en-US",
            CopyTemplates = appOptions.CopyTemplates ?? ["%1"],
            DefaultProvider = appOptions.LastProvider ?? "Google",
            Formality = appOptions.Formality ?? "Default",
            Context = appOptions.Context ?? "",
        };
    }
}
