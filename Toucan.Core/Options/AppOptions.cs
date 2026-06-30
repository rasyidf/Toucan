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
    public string AppLanguage { get; set; } = "en-US";
    public int PageSize { get; set; } = 15;
    public int MaxItems { get; set; } = 5000;
    public int TruncateResultsOver { get; set; } = 5000;
    public int LoadingDepth { get; set; } = 1;

    // --- Machine Translation ---
    public string Formality { get; set; } = "Default";
    public string Context { get; set; } = string.Empty;
    public string LastProvider { get; set; } = "Google";

    // --- Copy Templates ---
    public string CopyTemplate1 { get; set; } = "%1";
    public string CopyTemplate2 { get; set; } = "{ this.props.t('%1') }";
    public string CopyTemplate3 { get; set; } = "{ t('%1') }";

    // --- Editor behavior ---
    public bool PlainTextKeys { get; set; }
    public List<string> FilterHistory { get; set; } = [];
    public List<string> SuggestedLanguages { get; set; } = ["en-US", "id-ID", "zh-CN", "fr-FR", "es-ES"];
    // --- Last session state ---
    public string? LastProjectPath { get; set; }

    private static readonly string s_path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan");

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
