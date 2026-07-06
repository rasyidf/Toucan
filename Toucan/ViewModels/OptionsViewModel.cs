using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Services;

namespace Toucan.ViewModels;

public partial class OptionsViewModel : ObservableObject
{
    private readonly IPreferenceService _preferenceService;
    private readonly IProjectDefaultsService _defaultsService;
    private readonly IDialogService _dialogService;

    public OptionsViewModel(IPreferenceService preferenceService, IProjectDefaultsService defaultsService, IDialogService dialogService)
    {
        _preferenceService = preferenceService;
        _defaultsService = defaultsService;
        _dialogService = dialogService;
        AppOptions = _preferenceService.Load();
        ProjectDefaults = _defaultsService.Load();

        LoadFromOptions();
    }

    // --- Backing models ---
    [ObservableProperty] private AppOptions appOptions;
    [ObservableProperty] private ProjectDefaults projectDefaults;

    // ==========================================================
    // GENERAL PAGE
    // ==========================================================
    [ObservableProperty] private string appLanguage = "en-US";
    [ObservableProperty] private bool openLastProjectOnStartup = true;
    [ObservableProperty] private string pageSizeText = "15";
    [ObservableProperty] private string maxItemsText = "5000";
    [ObservableProperty] private string truncateSizeText = "5000";
    [ObservableProperty] private string loadingDepthText = "1";

    // ==========================================================
    // APPEARANCE PAGE
    // ==========================================================
    [ObservableProperty] private string theme = "System";
    [ObservableProperty] private string backdropType = "Mica";
    [ObservableProperty] private string fontSizeText = "13";

    // ==========================================================
    // EDITOR PAGE
    // ==========================================================
    [ObservableProperty] private bool plainTextKeys;
    [ObservableProperty] private string defaultLanguage = "en-US";

    /// <summary>CRUD list of copy templates (1-5).</summary>
    public ObservableCollection<CopyTemplateItem> CopyTemplates { get; } = [];
    public bool CanAddCopyTemplate => CopyTemplates.Count < 5;

    [RelayCommand]
    private void AddCopyTemplate()
    {
        if (CopyTemplates.Count < 5)
        {
            CopyTemplates.Add(new CopyTemplateItem(""));
            OnPropertyChanged(nameof(CanAddCopyTemplate));
        }
    }

    [RelayCommand]
    private void RemoveCopyTemplate(CopyTemplateItem? item)
    {
        if (item != null && CopyTemplates.Count > 1)
        {
            CopyTemplates.Remove(item);
            OnPropertyChanged(nameof(CanAddCopyTemplate));
        }
    }

    // ==========================================================
    // TRANSLATION PAGE
    // ==========================================================
    [ObservableProperty] private string formality = "Default";
    [ObservableProperty] private string context = string.Empty;
    [ObservableProperty] private bool preservePlaceholders = true;
    [ObservableProperty] private bool previewBeforeApply = true;

    // ==========================================================
    // VALIDATION PAGE (project defaults)
    // ==========================================================
    [ObservableProperty] private bool validateOnSave = true;
    [ObservableProperty] private bool ruleMissingTranslationEnabled = true;
    [ObservableProperty] private string ruleMissingTranslationSeverity = "Warning";
    [ObservableProperty] private bool rulePlaceholderMismatchEnabled = true;
    [ObservableProperty] private string rulePlaceholderMismatchSeverity = "Error";
    [ObservableProperty] private bool ruleDuplicateKeyEnabled = true;
    [ObservableProperty] private string ruleDuplicateKeySeverity = "Error";
    [ObservableProperty] private bool ruleUntranslatedCopyEnabled = true;
    [ObservableProperty] private string ruleUntranslatedCopySeverity = "Info";
    [ObservableProperty] private bool ruleEmptyValueEnabled = true;
    [ObservableProperty] private string ruleEmptyValueSeverity = "Warning";
    [ObservableProperty] private bool ruleWhitespaceMismatchEnabled = true;
    [ObservableProperty] private string ruleWhitespaceMismatchSeverity = "Info";

    // ==========================================================
    // TRANSLATION MEMORY PAGE
    // ==========================================================
    [ObservableProperty] private double tmSimilarityThreshold = 0.7;
    [ObservableProperty] private int tmGlobalScopeIndex; // 0 = Global, 1 = Project only
    [ObservableProperty] private bool tmAutoSuggest = true;
    [ObservableProperty] private string tmMaxSuggestionsText = "5";

    // ==========================================================
    // SOURCE CODE PAGE (project defaults)
    // ==========================================================
    [ObservableProperty] private bool defaultAutoScanOnOpen;
    [ObservableProperty] private string defaultScanExtensions = string.Empty;
    [ObservableProperty] private string defaultExcludedDirectories = string.Empty;
    [ObservableProperty] private string defaultExternalEditor = "code --goto {file}:{line}";
    [ObservableProperty] private string defaultLocaleFolderPattern = "locales";
    [ObservableProperty] private string defaultKeyMatcherPattern = string.Empty;
    [ObservableProperty] private string selectedFrameworkPreset = "Custom";

    /// <summary>
    /// Applies a framework preset: populates scan extensions, excluded dirs, locale folder,
    /// and key matcher from the selected framework profile conventions.
    /// </summary>
    [RelayCommand]
    private void ApplyFrameworkPreset()
    {
        // ponytail: maps framework display name → scan config. Avoids coupling to IFrameworkProfile at settings level.
        (string extensions, string excluded, string locale, string matcher) = SelectedFrameworkPreset switch
        {
            "i18next (React/Next.js)" => (".ts, .tsx, .js, .jsx, .vue, .svelte", "node_modules, .git, dist, build, .next", "locales", @"t\('([^']+)'\)|useTranslation\(\)"),
            "Android" => (".kt, .java, .xml", ".git, build, .gradle", "res", @"getString\(R\.string\.([^)]+)\)"),
            "Flutter (ARB)" => (".dart", ".git, build, .dart_tool", "lib/l10n", @"AppLocalizations\.of\(context\)\.(\w+)"),
            ".NET (RESX)" => (".cs, .cshtml, .razor", ".git, bin, obj", "Resources", @"@?Localizer\[""([^""]+)""\]|GetString\(""([^""]+)""\)"),
            "iOS (Strings)" => (".swift, .m", ".git, build, Pods", "*.lproj", @"NSLocalizedString\(""([^""]+)"""),
            "Rails (YAML)" => (".rb, .erb, .haml", ".git, node_modules, tmp", "config/locales", @"t\('([^']+)'\)|I18n\.t\('([^']+)'\)"),
            "Gettext (PO)" => (".py, .php, .rb", ".git, node_modules, __pycache__", "locale", @"_\(""([^""]+)""\)|gettext\(""([^""]+)""\)"),
            "Generic JSON" => (".ts, .tsx, .js, .jsx, .py, .cs", "node_modules, .git, dist, build, bin, obj", "locales", @"t\('([^']+)'\)"),
            _ => ("", "", "", "")
        };

        if (!string.IsNullOrEmpty(extensions)) DefaultScanExtensions = extensions;
        if (!string.IsNullOrEmpty(excluded)) DefaultExcludedDirectories = excluded;
        if (!string.IsNullOrEmpty(locale)) DefaultLocaleFolderPattern = locale;
        if (!string.IsNullOrEmpty(matcher)) DefaultKeyMatcherPattern = matcher;
    }

    // ==========================================================
    // LANGUAGES PAGE
    // ==========================================================
    [ObservableProperty] private ObservableCollection<string> suggestedLanguages = [];

    /// <summary>LanguageEntry wrappers for the shared LanguageListEditor component.</summary>
    public ObservableCollection<LanguageEntry> SuggestedLanguageEntries { get; } = [];

    // ==========================================================
    // LEGACY: project-specific editor config (via OptionsDialog code-behind)
    // ==========================================================
    [ObservableProperty] private string projectFilePath = string.Empty;
    [ObservableProperty] private string projectPrimaryLanguage = string.Empty;
    [ObservableProperty] private bool projectSaveEmptyTranslations = true;
    [ObservableProperty] private string projectTranslationOrder = "Alphabetically sorted";
    [ObservableProperty] private string projectCopyTemplate1 = string.Empty;
    [ObservableProperty] private string projectCopyTemplate2 = string.Empty;
    [ObservableProperty] private string projectCopyTemplate3 = string.Empty;
    [ObservableProperty] private bool applyChangesToProject;

    // ==========================================================
    // LEGACY: unused but kept for compat with existing code-behind
    // ==========================================================
    [ObservableProperty] private string indent = "tab";
    [ObservableProperty] private string format = "Json";
    [ObservableProperty] private bool supportArrays;
    [ObservableProperty] private bool saveEmptyTranslations = true;
    [ObservableProperty] private string translationOrder = "Alphabetically sorted";
    [ObservableProperty] private string sourceRoot = string.Empty;
    [ObservableProperty] private string sourceCodeEditor = string.Empty;
    [ObservableProperty] private string editorParameters = string.Empty;

    public int ContextCharCount => Context?.Length ?? 0;

    public Action<bool?>? CloseAction { get; set; }

    // ==========================================================
    // LOAD
    // ==========================================================
    private void LoadFromOptions()
    {
        var opts = AppOptions;
        var defs = ProjectDefaults;

        // General
        AppLanguage = opts.AppLanguage ?? "en-US";
        OpenLastProjectOnStartup = opts.OpenLastProjectOnStartup;
        PageSizeText = opts.PageSize.ToString();
        MaxItemsText = opts.MaxItems.ToString();
        TruncateSizeText = opts.TruncateResultsOver.ToString();
        LoadingDepthText = opts.LoadingDepth.ToString();

        // Appearance
        Theme = opts.Theme ?? "System";
        BackdropType = opts.BackdropType ?? "Mica";
        FontSizeText = opts.FontSize.ToString();

        // Editor
        PlainTextKeys = opts.PlainTextKeys;
        DefaultLanguage = opts.DefaultLanguage ?? "en-US";
        CopyTemplates.Clear();
        foreach (var t in opts.CopyTemplates ?? ["%1"])
            CopyTemplates.Add(new CopyTemplateItem(t));
        if (CopyTemplates.Count == 0) CopyTemplates.Add(new CopyTemplateItem("%1"));
        OnPropertyChanged(nameof(CanAddCopyTemplate));

        // Translation (from project defaults)
        Formality = defs.Formality ?? "Default";
        Context = defs.Context ?? "";
        PreservePlaceholders = defs.PreservePlaceholders;
        PreviewBeforeApply = defs.PreviewBeforeApply;

        // Validation (from project defaults)
        ValidateOnSave = defs.ValidateOnSave;
        LoadValidationRules(defs.ValidationRules);

        // Translation Memory
        TmSimilarityThreshold = opts.TmSimilarityThreshold;
        TmGlobalScopeIndex = opts.TmGlobalScope ? 0 : 1;
        TmAutoSuggest = opts.TmAutoSuggest;
        TmMaxSuggestionsText = opts.TmMaxSuggestions.ToString();

        // Source Code (from project defaults)
        DefaultAutoScanOnOpen = defs.AutoScanOnOpen;
        DefaultScanExtensions = string.Join(", ", defs.ScanExtensions ?? []);
        DefaultExcludedDirectories = string.Join(", ", defs.ExcludedDirectories ?? []);
        DefaultExternalEditor = defs.ExternalEditor ?? "code --goto {file}:{line}";
        DefaultLocaleFolderPattern = defs.LocaleFolderPattern ?? "locales";
        DefaultKeyMatcherPattern = defs.KeyMatcherPattern ?? "";
        SelectedFrameworkPreset = "Custom";

        // Languages
        SuggestedLanguages = new ObservableCollection<string>(opts.SuggestedLanguages ?? ["en-US"]);
        SuggestedLanguageEntries.Clear();
        foreach (var code in SuggestedLanguages)
        {
            SuggestedLanguageEntries.Add(new LanguageEntry
            {
                Code = code,
                DisplayName = GetDisplayName(code),
                CanRemove = true,
            });
        }
    }

    private void LoadValidationRules(Dictionary<string, ValidationRuleConfig> rules)
    {
        RuleMissingTranslationEnabled = rules.GetValueOrDefault("missing-translation", new(true, "Warning")).Enabled;
        RuleMissingTranslationSeverity = rules.GetValueOrDefault("missing-translation", new(true, "Warning")).Severity;
        RulePlaceholderMismatchEnabled = rules.GetValueOrDefault("placeholder-mismatch", new(true, "Error")).Enabled;
        RulePlaceholderMismatchSeverity = rules.GetValueOrDefault("placeholder-mismatch", new(true, "Error")).Severity;
        RuleDuplicateKeyEnabled = rules.GetValueOrDefault("duplicate-key", new(true, "Error")).Enabled;
        RuleDuplicateKeySeverity = rules.GetValueOrDefault("duplicate-key", new(true, "Error")).Severity;
        RuleUntranslatedCopyEnabled = rules.GetValueOrDefault("untranslated-copy", new(true, "Info")).Enabled;
        RuleUntranslatedCopySeverity = rules.GetValueOrDefault("untranslated-copy", new(true, "Info")).Severity;
        RuleEmptyValueEnabled = rules.GetValueOrDefault("empty-value", new(true, "Warning")).Enabled;
        RuleEmptyValueSeverity = rules.GetValueOrDefault("empty-value", new(true, "Warning")).Severity;
        RuleWhitespaceMismatchEnabled = rules.GetValueOrDefault("whitespace-mismatch", new(true, "Info")).Enabled;
        RuleWhitespaceMismatchSeverity = rules.GetValueOrDefault("whitespace-mismatch", new(true, "Info")).Severity;
    }

    // ==========================================================
    // SAVE
    // ==========================================================
    [RelayCommand]
    private void Save()
    {
        // --- AppOptions ---
        if (!int.TryParse(PageSizeText, out int page)) page = AppOptions.PageSize;
        if (!int.TryParse(TruncateSizeText, out int trunc)) trunc = AppOptions.TruncateResultsOver;
        if (!int.TryParse(MaxItemsText, out int maxItems)) maxItems = AppOptions.MaxItems;
        if (!int.TryParse(LoadingDepthText, out int depth)) depth = 1;
        if (!double.TryParse(FontSizeText, out double fontSize)) fontSize = 13;
        if (!int.TryParse(TmMaxSuggestionsText, out int tmMax)) tmMax = 5;

        AppOptions.AppLanguage = AppLanguage;
        AppOptions.OpenLastProjectOnStartup = OpenLastProjectOnStartup;
        AppOptions.PageSize = page;
        AppOptions.MaxItems = maxItems;
        AppOptions.TruncateResultsOver = trunc;
        AppOptions.LoadingDepth = Math.Clamp(depth, 1, 5);
        AppOptions.Theme = Theme;
        AppOptions.BackdropType = BackdropType;
        AppOptions.FontSize = Math.Clamp(fontSize, 11, 18);
        AppOptions.PlainTextKeys = PlainTextKeys;
        AppOptions.DefaultLanguage = DefaultLanguage;
        AppOptions.CopyTemplates = [.. CopyTemplates.Select(t => t.Value)];
        AppOptions.TmSimilarityThreshold = TmSimilarityThreshold;
        AppOptions.TmGlobalScope = TmGlobalScopeIndex == 0;
        AppOptions.TmAutoSuggest = TmAutoSuggest;
        AppOptions.TmMaxSuggestions = Math.Clamp(tmMax, 1, 10);
        AppOptions.SuggestedLanguages = [.. SuggestedLanguageEntries.Select(e => e.Code)];

        _preferenceService.Save(AppOptions);

        // --- ProjectDefaults ---
        ProjectDefaults.Formality = Formality;
        ProjectDefaults.Context = Context;
        ProjectDefaults.PreservePlaceholders = PreservePlaceholders;
        ProjectDefaults.PreviewBeforeApply = PreviewBeforeApply;
        ProjectDefaults.ValidateOnSave = ValidateOnSave;
        ProjectDefaults.ValidationRules = BuildValidationRules();
        ProjectDefaults.AutoScanOnOpen = DefaultAutoScanOnOpen;
        ProjectDefaults.ScanExtensions = ParseCommaSeparated(DefaultScanExtensions);
        ProjectDefaults.ExcludedDirectories = ParseCommaSeparated(DefaultExcludedDirectories);
        ProjectDefaults.ExternalEditor = DefaultExternalEditor;
        ProjectDefaults.LocaleFolderPattern = DefaultLocaleFolderPattern;
        ProjectDefaults.KeyMatcherPattern = DefaultKeyMatcherPattern;

        _defaultsService.Save(ProjectDefaults);

        // --- Legacy: project manifest write (if requested) ---
        if (ApplyChangesToProject && !string.IsNullOrWhiteSpace(ProjectFilePath))
        {
            WriteProjectManifest();
        }

        CloseAction?.Invoke(true);
    }

    private Dictionary<string, ValidationRuleConfig> BuildValidationRules() => new()
    {
        ["missing-translation"] = new(RuleMissingTranslationEnabled, RuleMissingTranslationSeverity),
        ["placeholder-mismatch"] = new(RulePlaceholderMismatchEnabled, RulePlaceholderMismatchSeverity),
        ["duplicate-key"] = new(RuleDuplicateKeyEnabled, RuleDuplicateKeySeverity),
        ["untranslated-copy"] = new(RuleUntranslatedCopyEnabled, RuleUntranslatedCopySeverity),
        ["empty-value"] = new(RuleEmptyValueEnabled, RuleEmptyValueSeverity),
        ["whitespace-mismatch"] = new(RuleWhitespaceMismatchEnabled, RuleWhitespaceMismatchSeverity),
    };

    private static List<string> ParseCommaSeparated(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private void WriteProjectManifest()
    {
        try
        {
            var manifestPath = System.IO.Path.Combine(ProjectFilePath, "toucan.tproj");
            if (!System.IO.File.Exists(manifestPath)) return;

            var text = System.IO.File.ReadAllText(manifestPath);
            JsonObject root = JsonNode.Parse(text)?.AsObject() ?? [];

            JsonObject editorCfg = root["editorConfiguration"]?.AsObject() ?? [];
            editorCfg["save_empty_translations"] = ProjectSaveEmptyTranslations.ToString().ToLowerInvariant();
            editorCfg["translation_order"] = ProjectTranslationOrder == "Primary language" ? "primary_language" : "alphabetical";
            editorCfg["copy_templates"] = new JsonArray(ProjectCopyTemplate1 ?? "", ProjectCopyTemplate2 ?? "", ProjectCopyTemplate3 ?? "");
            root["editorConfiguration"] = editorCfg;

            System.IO.File.WriteAllText(manifestPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    // ==========================================================
    // COMMANDS
    // ==========================================================
    [RelayCommand]
    private void RemoveSuggestedLanguage(string lang)
    {
        if (!string.IsNullOrEmpty(lang)) SuggestedLanguages.Remove(lang);
    }

    [RelayCommand]
    private void ConfigureLanguageCodes()
    {
        var input = _dialogService.ShowPrompt(
            "Add Languages",
            "Enter comma-separated language codes (e.g. fr-FR, de-DE, ja-JP):",
            string.Empty);
        if (string.IsNullOrWhiteSpace(input)) return;

        foreach (var code in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!SuggestedLanguages.Contains(code))
                SuggestedLanguages.Add(code);
        }
    }

    [RelayCommand]
    private void OpenProviderSettings()
    {
        _dialogService.ShowProviderSettings();
    }

    [RelayCommand]
    private void OpenProjectDefaults()
    {
        _dialogService.ShowProjectDefaults();
    }

    [RelayCommand]
    private void BrowseSourceRoot()
    {
        var result = _dialogService.SelectFolder(string.IsNullOrWhiteSpace(SourceRoot) ? null : SourceRoot);
        if (result is not null) SourceRoot = result;
    }

    [RelayCommand]
    private void BrowseSourceEditor()
    {
        var result = _dialogService.SelectFile(
            string.IsNullOrWhiteSpace(SourceCodeEditor) ? null : System.IO.Path.GetDirectoryName(SourceCodeEditor),
            "Executables (*.exe)|*.exe|All Files (*.*)|*.*");
        if (result is not null) SourceCodeEditor = result;
    }

    // --- Data & Privacy commands ---
    [RelayCommand]
    private void ClearFilterHistory()
    {
        AppOptions.FilterHistory.Clear();
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        // ponytail: actual clearing handled by caller via IRecentProjectService after dialog confirms
    }

    [RelayCommand]
    private void ClearTranslationMemory()
    {
        // ponytail: actual TM clearing delegated to TM service after confirmation
    }

    [RelayCommand]
    private void ExportSettings()
    {
        // ponytail: export both AppOptions + ProjectDefaults to a single JSON file
    }

    [RelayCommand]
    private void ImportSettings()
    {
        // ponytail: import from backup JSON and reload
    }

    [RelayCommand]
    private void ResetAllSettings()
    {
        // ponytail: reset to factory defaults after confirmation
    }

    [RelayCommand]
    private void ImportTm()
    {
        // ponytail: import TMX file via ITranslationMemory
    }

    [RelayCommand]
    private void ExportTm()
    {
        // ponytail: export TM to TMX file
    }

    // --- About commands ---
    [RelayCommand]
    private void CheckForUpdates()
    {
        // ponytail: placeholder for auto-updater (v1.1 roadmap). Currently opens releases page.
        OpenUrl("https://github.com/rasyidf/Toucan/releases");
    }

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }

    [RelayCommand]
    private void CopyVersion()
    {
        try { System.Windows.Clipboard.SetText("Toucan 0.17.0"); } catch { }
    }

    [RelayCommand]
    private void OpenLogLocation()
    {
        var logDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logDir) { UseShellExecute = true }); }
        catch { }
    }

    private static string GetDisplayName(string code)
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo(code).DisplayName; }
        catch { return code; }
    }
}
