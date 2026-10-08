using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Toucan.Core.Options;

/// <summary>
/// Global application preferences. Stored in ~/Documents/Toucan/settings.json.
/// NOT project-specific — project settings live in toucan.project.
/// </summary>
public class AppOptions
{
    // --- UI preferences ---
    public string DefaultLanguage { get; set; } = "en-US";
    public string Theme { get; set; } = "System";
    public string BackdropType { get; set; } = "Mica";
    /// <summary>Name of the selected color scheme preset, or "Custom" when <see cref="AccentColor"/> matches no preset.</summary>
    public string ColorScheme { get; set; } = "Toucan";
    /// <summary>Accent color as #RRGGBB; null uses the preset's own accent.</summary>
    public string? AccentColor { get; set; }
    /// <summary>User edits to individual theme colors, keyed "Light:CardBackgroundBrush" / "Dark:CardBackgroundBrush", values #RRGGBB.</summary>
    public Dictionary<string, string> SchemeColors { get; set; } = [];
    /// <summary>"Stable" or "Preview": which GitHub releases "Check for updates" considers.</summary>
    public string UpdateChannel { get; set; } = "Stable";
    public string AppLanguage { get; set; } = "en-US";
    public double FontSize { get; set; } = 13;
    public int PageSize { get; set; } = 15;
    public int MaxItems { get; set; } = 5000;
    public int TruncateResultsOver { get; set; } = 5000;
    public int LoadingDepth { get; set; } = 1;

    // --- Machine Translation (AI settings are in ai.json, see AiSettings) ---
    public string Formality { get; set; } = "Default";
    public string Context { get; set; } = string.Empty;
    public string LastProvider { get; set; } = "Google";

    // --- Copy Templates (1-5 dynamic list) ---
    public List<string> CopyTemplates { get; set; } = ["%1", "{ this.props.t('%1') }", "{ t('%1') }"];

    // --- Editor behavior ---
    public bool PlainTextKeys { get; set; }
    public List<string> FilterHistory { get; set; } = [];
    public List<string> SuggestedLanguages { get; set; } = ["en-US", "id-ID", "zh-CN", "fr-FR", "es-ES"];

    // --- Translation Memory ---
    public double TmSimilarityThreshold { get; set; } = 0.7;
    public bool TmGlobalScope { get; set; } = true;
    public bool TmAutoSuggest { get; set; } = true;
    public int TmMaxSuggestions { get; set; } = 5;

    // --- Recent projects ---
    public int RecentProjectsLimit { get; set; } = 10;
    public bool ClearRecentKeepsPinned { get; set; } = true;
    /// <summary>Use the language of the most recently opened project instead of <see cref="DefaultLanguage"/>.</summary>
    public bool DetectLanguageFromRecent { get; set; }

    // --- Default project languages ---
    public List<string> DefaultProjectLanguages { get; set; } = ["en-US"];
    // --- Onboarding ---
    /// <summary>The onboarding version the user has completed; 0 means first run. Onboarding asks whether to use AI.</summary>
    public int OnboardingVersion { get; set; }

    // --- Last session state ---
    public string? LastProjectPath { get; set; }
    public bool OpenLastProjectOnStartup { get; set; } = true;

    private static readonly string s_path = Path.Combine(
        Toucan.Core.Services.UserDataFolder.Root, "Toucan");

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public static AppOptions LoadFromDisk()
    {
        var file = Path.Combine(s_path, "settings.json");
        if (!File.Exists(file)) return new AppOptions();

        try
        {
            var options = JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(file), s_options) ?? new AppOptions();
            if (options.PageSize <= 0) options.PageSize = 100;
            if (options.MaxItems <= 0) options.MaxItems = 100;
            if (options.TruncateResultsOver <= 0) options.TruncateResultsOver = 2000;
            if (options.LoadingDepth <= 0) options.LoadingDepth = 1;
            if (options.RecentProjectsLimit <= 0) options.RecentProjectsLimit = 10;
            return options;
        }
        catch { return new AppOptions(); }
    }

    public void ToDisk()
    {
        Directory.CreateDirectory(s_path);
        File.WriteAllText(Path.Combine(s_path, "settings.json"), JsonSerializer.Serialize(this, s_options));
    }
}
