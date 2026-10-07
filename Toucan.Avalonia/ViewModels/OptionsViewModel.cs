using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Core.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Application preferences (AppOptions) plus the defaults applied to new projects (ProjectDefaults).</summary>
public partial class OptionsViewModel : ObservableObject
{
    private readonly IPreferenceService _preferenceService;
    private readonly IValidationPipeline? _validationPipeline;
    private readonly IProjectDefaultsService _defaultsService;
    private readonly IDialogService _dialogService;
    private readonly IAsyncMessageService _messages;
    private readonly IRecentProjectService? _recentProjects;
    private readonly ITranslationMemory? _translationMemory;
    private readonly ISecretService? _secrets;

    public OptionsViewModel(
        IPreferenceService preferenceService,
        IProjectDefaultsService defaultsService,
        IDialogService dialogService,
        IAsyncMessageService messages,
        IRecentProjectService? recentProjects = null,
        ITranslationMemory? translationMemory = null,
        IPluginCatalog? pluginCatalog = null,
        IPluginPolicyStore? pluginPolicy = null,
        IValidationPipeline? validationPipeline = null,
        AiSettingsViewModel? ai = null,
        ISecretService? secrets = null)
    {
        Ai = ai;
        _secrets = secrets;
        RefreshStoredSecrets();
        _validationPipeline = validationPipeline;
        _preferenceService = preferenceService;
        _defaultsService = defaultsService;
        _dialogService = dialogService;
        _messages = messages;
        _recentProjects = recentProjects;
        _translationMemory = translationMemory;
        appOptions = _preferenceService.Load();
        projectDefaults = _defaultsService.Load();
        LoadFromOptions();
        RefreshIntegration();

        foreach (var module in pluginCatalog?.BuiltInModules ?? [])
            BuiltInModules.Add(new BuiltInModuleItemViewModel(module));
        foreach (var plugin in pluginCatalog?.Plugins ?? [])
            Plugins.Add(new PluginItemViewModel(plugin, pluginPolicy, messages, () => PluginsChanged = true));
    }

    public static IReadOnlyList<string> Pages { get; } =
        ["General", "Appearance", "Editor", "Translation", "AI", "Validation", "Translation Memory", "Source Code", "Languages", "Shortcuts", "Integration", "Data & Privacy", "Plugins", "About"];

    /// <summary>Sidebar entries, in the same order as <see cref="Pages"/>: title, icon and tile color.</summary>
    public static IReadOnlyList<SettingsNavEntry> NavEntries { get; } =
    [
        new("General", "Settings", "#8E8E93"),
        new("Appearance", "DarkTheme", "#5856D6"),
        new("Editor", "Edit", "#007AFF"),
        new("Translation", "Character", "#34C759"),
        new("AI", "StarEmphasis", "#7D5BED"),
        new("Validation", "Accept", "#FF9500"),
        new("Translation Memory", "Library", "#AF52DE"),
        new("Source Code", "CodeHTML", "#5AC8FA"),
        new("Languages", "Globe", "#0A84FF"),
        new("Shortcuts", "Keyboard", "#636366"),
        new("Integration", "Link", "#30B0C7"),
        new("Data & Privacy", "Permissions", "#FF3B30"),
        new("Plugins", "AllApps", "#FF2D55"),
        new("About", "Help", "#8E8E93"),
    ];

    public int PageIndexOf(string page) => Pages.ToList().IndexOf(page);

    /// <summary>Index of the AI page in <see cref="Pages"/>.</summary>
    public const int AiPage = 4;

    /// <summary>Index of the Plugins page in <see cref="Pages"/>.</summary>
    public const int PluginsPage = 12;

    /// <summary>Settings → AI: the app-wide switch, the AI service and the editable prompts. Null in tests that do not need it.</summary>
    public AiSettingsViewModel? Ai { get; }

    // ───────────────────────── Secrets ─────────────────────────

    /// <summary>Names of the stored secrets (never their values), for Data &amp; privacy.</summary>
    public ObservableCollection<string> StoredSecrets { get; } = [];
    public bool HasStoredSecrets => StoredSecrets.Count > 0;
    public bool HasNoStoredSecrets => StoredSecrets.Count == 0;

    private void RefreshStoredSecrets()
    {
        StoredSecrets.Clear();
        foreach (var key in _secrets?.Keys() ?? []) StoredSecrets.Add(key);
        OnPropertyChanged(nameof(HasStoredSecrets));
        OnPropertyChanged(nameof(HasNoStoredSecrets));
    }

    [RelayCommand]
    private void RemoveSecret(string? key)
    {
        if (key == null || _secrets == null) return;
        _secrets.Remove(key);
        RefreshStoredSecrets();
    }

    [RelayCommand]
    private async Task RemoveAllSecrets()
    {
        if (_secrets == null || StoredSecrets.Count == 0) return;
        if (!await _messages.ConfirmAsync("Remove every stored API key and token? Providers and AI stop working until you enter them again.", "Remove Secrets")) return;
        _secrets.RemoveAll(string.Empty);
        RefreshStoredSecrets();
    }

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];

    /// <summary>Modules compiled into Toucan, listed read-only under the plugins.</summary>
    public ObservableCollection<BuiltInModuleItemViewModel> BuiltInModules { get; } = [];
    public bool HasBuiltInModules => BuiltInModules.Count > 0;
    public bool HasPlugins => Plugins.Count > 0;
    public bool HasNoPlugins => Plugins.Count == 0;
    public string PluginsFolder => PluginHostOptions.DefaultRoot();

    /// <summary>Set once a plugin was trusted, revoked, enabled or disabled; those take effect after a restart.</summary>
    [ObservableProperty] private bool pluginsChanged;

    /// <summary>Text typed in the settings search box. Filtering itself happens in the dialog, which knows the rendered rows.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSearching), nameof(HasNoSearchResults))] private string searchText = string.Empty;

    /// <summary>Per page: does it contain a matching setting? Set by the dialog.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNoSearchResults))] private IReadOnlyList<bool>? searchMatches;

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);
    public bool HasNoSearchResults => IsSearching && SearchMatches is { } m && !m.Contains(true);

    // ───────────────────────── System integration ─────────────────────────

    public bool CanManageAssociation => FileAssociationService.IsSupported;
    public bool CanManageFolderEntry => FileAssociationService.FolderEntrySupported;
    public string SettingsFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AssociationStatus))]
    private bool associationInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderEntryStatus))]
    private bool folderEntryInstalled;

    /// <summary>Install result or platform guidance (macOS has no run-time registration).</summary>
    [ObservableProperty] private string associationNote = string.Empty;

    public string AssociationStatus => AssociationInstalled ? "Installed" : "Not installed";
    public string FolderEntryStatus => FolderEntryInstalled ? "Installed" : "Not installed";

    private void RefreshIntegration()
    {
        AssociationInstalled = FileAssociationService.IsInstalled();
        FolderEntryInstalled = FileAssociationService.IsFolderEntryInstalled();
        if (!FileAssociationService.IsSupported) AssociationNote = FileAssociationService.UnsupportedReason;
    }

    private void ApplyIntegration(IntegrationResult result)
    {
        RefreshIntegration();
        if (!result.Ok) AssociationNote = result.Error ?? "The change failed.";
        else if (FileAssociationService.IsSupported) AssociationNote = string.Empty;
    }

    [RelayCommand] private void InstallAssociation() => ApplyIntegration(FileAssociationService.Install());
    [RelayCommand] private void RemoveAssociation() => ApplyIntegration(FileAssociationService.Uninstall());
    [RelayCommand] private void InstallFolderEntry() => ApplyIntegration(FileAssociationService.InstallFolderEntry());
    [RelayCommand] private void RemoveFolderEntry() => ApplyIntegration(FileAssociationService.UninstallFolderEntry());

    public static IReadOnlyList<string> ThemeOptions { get; } = ["System", "Light", "Dark"];
    public static IReadOnlyList<string> FormalityOptions { get; } = ["Default", "More", "Less", "Formal", "Informal"];
    public static IReadOnlyList<string> SeverityOptions { get; } = ["Error", "Warning", "Info"];
    public static IReadOnlyList<string> TmScopeOptions { get; } = ["All projects", "Current project only"];
    public static IReadOnlyList<AppLanguageOption> AppLanguageOptions { get; } = [.. Locales.Loc.Available.Select(c => new AppLanguageOption(c, Locales.Loc.NativeName(c)))];

    /// <summary>The interface language as a picker item; <see cref="AppLanguage"/> keeps the plain code that is saved.</summary>
    public AppLanguageOption? SelectedAppLanguage
    {
        get => AppLanguageOptions.FirstOrDefault(o => string.Equals(o.Code, AppLanguage, StringComparison.OrdinalIgnoreCase));
        set { if (value != null) AppLanguage = value.Code; }
    }

    partial void OnAppLanguageChanged(string value) => OnPropertyChanged(nameof(SelectedAppLanguage));

    public static IReadOnlyList<string> FrameworkPresets { get; } =
    [
        "Custom", "i18next (React/Next.js)", "Android", "Flutter (ARB)", ".NET (RESX)", "iOS (Strings)", "Rails (YAML)", "Gettext (PO)", "Generic JSON"
    ];

    public IReadOnlyList<KeybindingEntry> Shortcuts { get; } = KeybindingService.GetDefinitions();

    /// <summary>Shortcuts grouped by category for the grouped-list layout.</summary>
    public IReadOnlyList<ShortcutGroup> ShortcutGroups { get; } =
        KeybindingService.GetDefinitions().GroupBy(k => k.Category).Select(g => new ShortcutGroup(g.Key, g.ToList())).ToList();

    [ObservableProperty] private int selectedPageIndex;

    [ObservableProperty] private AppOptions appOptions;
    [ObservableProperty] private ProjectDefaults projectDefaults;

    // General
    [ObservableProperty] private string appLanguage = "en-US";
    [ObservableProperty] private bool openLastProjectOnStartup = true;
    [ObservableProperty] private decimal pageSize = 15;

    // Recent projects
    [ObservableProperty] private decimal recentProjectsLimit = 10;
    [ObservableProperty] private bool clearRecentKeepsPinned = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DetectedLanguageText))] private bool detectLanguageFromRecent;

    /// <summary>The recent list as shown on the Recent projects page; rebuilt after every pin or remove.</summary>
    public ObservableCollection<Project> RecentItems { get; } = [];
    public bool HasRecentItems => RecentItems.Count > 0;
    public bool HasNoRecentItems => RecentItems.Count == 0;

    /// <summary>What detection would pick right now, shown next to the toggle.</summary>
    public string DetectedLanguageText
    {
        get
        {
            var detected = PreferredLanguageDetector.Detect(RecentItems);
            return detected == null ? "No recent project has a language yet." : $"Most recent project uses {detected}.";
        }
    }
    [ObservableProperty] private decimal maxItems = 5000;
    [ObservableProperty] private decimal truncateSize = 5000;
    [ObservableProperty] private decimal loadingDepth = 1;

    // Appearance
    [ObservableProperty] private string theme = "System";
    [ObservableProperty] private decimal fontSize = 13;

    // Editor
    [ObservableProperty] private bool plainTextKeys;
    [ObservableProperty] private string defaultLanguage = "en-US";


    /// <summary>"English (United States) · en-US" for the button that opens the language picker.</summary>
    public string DefaultLanguageText => $"{Cultures.DisplayName(DefaultLanguage)} · {DefaultLanguage}";

    partial void OnDefaultLanguageChanged(string value) => OnPropertyChanged(nameof(DefaultLanguageText));

    [RelayCommand]
    private async Task ChooseDefaultLanguage()
    {
        var code = await _dialogService.ShowLanguagePromptAsync("Default Language", "Used as the source language when a project doesn't specify one.", null);
        if (!string.IsNullOrWhiteSpace(code)) DefaultLanguage = code;
    }
    public ObservableCollection<CopyTemplateItem> CopyTemplates { get; } = [];
    public bool CanAddCopyTemplate => CopyTemplates.Count < 5;

    // Translation
    [ObservableProperty] private string formality = "Default";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextCharCount))]
    private string context = string.Empty;

    [ObservableProperty] private bool preservePlaceholders = true;
    [ObservableProperty] private bool previewBeforeApply = true;
    public int ContextCharCount => Context?.Length ?? 0;

    // Validation
    [ObservableProperty] private bool validateOnSave = true;
    public ObservableCollection<ValidationRuleOption> ValidationRules { get; } = [];

    // Translation memory
    [ObservableProperty] private double tmSimilarityThreshold = 0.7;
    [ObservableProperty] private int tmGlobalScopeIndex;
    [ObservableProperty] private bool tmAutoSuggest = true;
    [ObservableProperty] private decimal tmMaxSuggestions = 5;
    public string TmEntryCount => _translationMemory == null ? "n/a" : _translationMemory.Count.ToString("N0", CultureInfo.CurrentCulture);

    // Source code
    [ObservableProperty] private bool defaultAutoScanOnOpen;
    [ObservableProperty] private string defaultScanExtensions = string.Empty;
    [ObservableProperty] private string defaultExcludedDirectories = string.Empty;
    [ObservableProperty] private string defaultExternalEditor = "code --goto \"{file}:{line}\"";
    [ObservableProperty] private string defaultLocaleFolderPattern = "locales";
    [ObservableProperty] private string defaultKeyMatcherPattern = string.Empty;
    [ObservableProperty] private string selectedFrameworkPreset = "Custom";

    // Languages
    public ObservableCollection<LanguageEntry> SuggestedLanguageEntries { get; } = [];

    // About
    public string VersionText { get; } = "Toucan " + (Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "dev");
    public string RuntimeText { get; } = $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription}";

    public Action<bool>? CloseAction { get; set; }

    private void LoadFromOptions()
    {
        var opts = AppOptions;
        var defs = ProjectDefaults;
        AppLanguage = opts.AppLanguage ?? "en-US";
        OpenLastProjectOnStartup = opts.OpenLastProjectOnStartup;
        RecentProjectsLimit = opts.RecentProjectsLimit;
        ClearRecentKeepsPinned = opts.ClearRecentKeepsPinned;
        DetectLanguageFromRecent = opts.DetectLanguageFromRecent;
        RefreshRecentItems();
        PageSize = opts.PageSize;
        MaxItems = opts.MaxItems;
        TruncateSize = opts.TruncateResultsOver;
        LoadingDepth = opts.LoadingDepth;
        Theme = ThemeOptions.Contains(opts.Theme) ? opts.Theme : "System";
        LoadSchemeFromOptions();
        FontSize = (decimal)opts.FontSize;
        PlainTextKeys = opts.PlainTextKeys;
        DefaultLanguage = opts.DefaultLanguage ?? "en-US";

        CopyTemplates.Clear();
        foreach (var t in opts.CopyTemplates is { Count: > 0 } list ? list : ["%1"]) CopyTemplates.Add(new CopyTemplateItem(t));
        OnPropertyChanged(nameof(CanAddCopyTemplate));

        Formality = FormalityOptions.Contains(defs.Formality) ? defs.Formality : "Default";
        Context = defs.Context ?? string.Empty;
        PreservePlaceholders = defs.PreservePlaceholders;
        PreviewBeforeApply = defs.PreviewBeforeApply;

        ValidateOnSave = defs.ValidateOnSave;
        ValidationRules.Clear();
        // Every registered rule (built-in modules and plugins) is listed; nothing here names a rule.
        foreach (var rule in _validationPipeline?.Rules ?? [])
        {
            var cfg = defs.ValidationRules.GetValueOrDefault(rule.Id, new ValidationRuleConfig(true, rule.DefaultSeverity.ToString()));
            ValidationRules.Add(new ValidationRuleOption(rule.Id, rule.Name) { Enabled = cfg.Enabled, Severity = cfg.Severity });
        }

        TmSimilarityThreshold = opts.TmSimilarityThreshold;
        TmGlobalScopeIndex = opts.TmGlobalScope ? 0 : 1;
        TmAutoSuggest = opts.TmAutoSuggest;
        TmMaxSuggestions = opts.TmMaxSuggestions;

        DefaultAutoScanOnOpen = defs.AutoScanOnOpen;
        DefaultScanExtensions = string.Join(", ", defs.ScanExtensions ?? []);
        DefaultExcludedDirectories = string.Join(", ", defs.ExcludedDirectories ?? []);
        DefaultExternalEditor = defs.ExternalEditor ?? "code --goto \"{file}:{line}\"";
        DefaultLocaleFolderPattern = defs.LocaleFolderPattern ?? "locales";
        DefaultKeyMatcherPattern = defs.KeyMatcherPattern ?? string.Empty;
        SelectedFrameworkPreset = "Custom";

        SuggestedLanguageEntries.Clear();
        foreach (var code in opts.SuggestedLanguages ?? ["en-US"])
            SuggestedLanguageEntries.Add(new LanguageEntry { Code = code, DisplayName = Cultures.DisplayName(code) });
    }

    [RelayCommand]
    private void Save()
    {
        Ai?.Save();
        AppOptions.AppLanguage = AppLanguage;
        AppOptions.OpenLastProjectOnStartup = OpenLastProjectOnStartup;
        AppOptions.RecentProjectsLimit = (int)Math.Clamp(RecentProjectsLimit, 1, 50);
        AppOptions.ClearRecentKeepsPinned = ClearRecentKeepsPinned;
        AppOptions.DetectLanguageFromRecent = DetectLanguageFromRecent;
        if (_recentProjects != null) _recentProjects.Limit = AppOptions.RecentProjectsLimit;
        AppOptions.PageSize = (int)Math.Max(1, PageSize);
        AppOptions.MaxItems = (int)Math.Max(1, MaxItems);
        AppOptions.TruncateResultsOver = (int)Math.Max(1, TruncateSize);
        AppOptions.LoadingDepth = (int)Math.Clamp(LoadingDepth, 1, 5);
        AppOptions.Theme = Theme;
        SaveSchemeToOptions();
        AppOptions.FontSize = (double)Math.Clamp(FontSize, 11, 18);
        AppOptions.PlainTextKeys = PlainTextKeys;
        AppOptions.DefaultLanguage = DefaultLanguage;
        AppOptions.CopyTemplates = [.. CopyTemplates.Select(t => t.Value)];
        AppOptions.TmSimilarityThreshold = Math.Round(TmSimilarityThreshold, 2);
        AppOptions.TmGlobalScope = TmGlobalScopeIndex == 0;
        AppOptions.TmAutoSuggest = TmAutoSuggest;
        AppOptions.TmMaxSuggestions = (int)Math.Clamp(TmMaxSuggestions, 1, 10);
        AppOptions.SuggestedLanguages = [.. SuggestedLanguageEntries.Select(e => e.Code)];
        _preferenceService.Save(AppOptions);

        ProjectDefaults.Formality = Formality;
        ProjectDefaults.Context = Context;
        ProjectDefaults.PreservePlaceholders = PreservePlaceholders;
        ProjectDefaults.PreviewBeforeApply = PreviewBeforeApply;
        ProjectDefaults.ValidateOnSave = ValidateOnSave;
        ProjectDefaults.ValidationRules = ValidationRules.ToDictionary(r => r.Id, r => new ValidationRuleConfig(r.Enabled, r.Severity));
        ProjectDefaults.AutoScanOnOpen = DefaultAutoScanOnOpen;
        ProjectDefaults.ScanExtensions = ParseList(DefaultScanExtensions);
        ProjectDefaults.ExcludedDirectories = ParseList(DefaultExcludedDirectories);
        ProjectDefaults.ExternalEditor = DefaultExternalEditor;
        ProjectDefaults.LocaleFolderPattern = DefaultLocaleFolderPattern;
        ProjectDefaults.KeyMatcherPattern = DefaultKeyMatcherPattern;
        _defaultsService.Save(ProjectDefaults);

        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseAction?.Invoke(false);

    [RelayCommand]
    private void AddCopyTemplate()
    {
        if (CopyTemplates.Count >= 5) return;
        CopyTemplates.Add(new CopyTemplateItem(string.Empty));
        OnPropertyChanged(nameof(CanAddCopyTemplate));
    }

    [RelayCommand]
    private void RemoveCopyTemplate(CopyTemplateItem? item)
    {
        if (item == null || CopyTemplates.Count <= 1) return;
        CopyTemplates.Remove(item);
        OnPropertyChanged(nameof(CanAddCopyTemplate));
    }

    [RelayCommand]
    private void ApplyFrameworkPreset()
    {
        var (extensions, excluded, locale, matcher) = SelectedFrameworkPreset switch
        {
            "i18next (React/Next.js)" => (".ts, .tsx, .js, .jsx, .vue, .svelte", "node_modules, .git, dist, build, .next", "locales", @"t\('([^']+)'\)|useTranslation\(\)"),
            "Android" => (".kt, .java, .xml", ".git, build, .gradle", "res", @"getString\(R\.string\.([^)]+)\)"),
            "Flutter (ARB)" => (".dart", ".git, build, .dart_tool", "lib/l10n", @"AppLocalizations\.of\(context\)\.(\w+)"),
            ".NET (RESX)" => (".cs, .cshtml, .razor", ".git, bin, obj", "Resources", @"@?Localizer\[""([^""]+)""\]|GetString\(""([^""]+)""\)"),
            "iOS (Strings)" => (".swift, .m", ".git, build, Pods", "*.lproj", @"NSLocalizedString\(""([^""]+)"""),
            "Rails (YAML)" => (".rb, .erb, .haml", ".git, node_modules, tmp", "config/locales", @"t\('([^']+)'\)|I18n\.t\('([^']+)'\)"),
            "Gettext (PO)" => (".py, .php, .rb", ".git, node_modules, __pycache__", "locale", @"_\(""([^""]+)""\)|gettext\(""([^""]+)""\)"),
            "Generic JSON" => (".ts, .tsx, .js, .jsx, .py, .cs", "node_modules, .git, dist, build, bin, obj", "locales", @"t\('([^']+)'\)"),
            _ => (string.Empty, string.Empty, string.Empty, string.Empty)
        };
        if (!string.IsNullOrEmpty(extensions)) DefaultScanExtensions = extensions;
        if (!string.IsNullOrEmpty(excluded)) DefaultExcludedDirectories = excluded;
        if (!string.IsNullOrEmpty(locale)) DefaultLocaleFolderPattern = locale;
        if (!string.IsNullOrEmpty(matcher)) DefaultKeyMatcherPattern = matcher;
    }

    [RelayCommand]
    private async Task AddSuggestedLanguage()
    {
        var code = await _dialogService.ShowLanguagePromptAsync("Add Suggested Language", "Languages offered by default when creating a project.", null);
        if (string.IsNullOrWhiteSpace(code) || SuggestedLanguageEntries.Any(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase))) return;
        SuggestedLanguageEntries.Add(new LanguageEntry { Code = code, DisplayName = Cultures.DisplayName(code) });
    }

    [RelayCommand]
    private void RemoveSuggestedLanguage(LanguageEntry? entry)
    {
        if (entry != null) SuggestedLanguageEntries.Remove(entry);
    }

    [RelayCommand]
    private Task OpenProviderSettings() => _dialogService.ShowProviderSettingsAsync();

    [RelayCommand]
    private async Task ClearFilterHistory()
    {
        AppOptions.FilterHistory.Clear();
        _preferenceService.Save(AppOptions);
        await _messages.ShowMessageAsync("Filter history cleared.", "Data & Privacy");
    }

    [RelayCommand]
    private async Task ClearRecentProjects()
    {
        if (_recentProjects == null) return;
        var prompt = ClearRecentKeepsPinned
            ? "Remove all unpinned projects from the recent list?"
            : "Remove all projects, including pinned ones, from the recent list?";
        if (!await _messages.ConfirmAsync(prompt, "Recent projects")) return;
        _recentProjects.Clear(ClearRecentKeepsPinned);
        RefreshRecentItems();
        await _messages.ShowMessageAsync("Recent projects cleared.", "Recent projects");
    }

    [RelayCommand]
    private void TogglePinRecentItem(Project? project)
    {
        if (project == null || _recentProjects == null) return;
        _recentProjects.SetPinned(project.Path, !project.IsPinned);
        RefreshRecentItems();
    }

    [RelayCommand]
    private void RemoveRecentItem(Project? project)
    {
        if (project == null || _recentProjects == null) return;
        _recentProjects.Remove(project.Path);
        RefreshRecentItems();
    }

    private void RefreshRecentItems()
    {
        RecentItems.Clear();
        foreach (var p in _recentProjects?.LoadRecent() ?? []) RecentItems.Add(p);
        OnPropertyChanged(nameof(HasRecentItems));
        OnPropertyChanged(nameof(HasNoRecentItems));
        OnPropertyChanged(nameof(DetectedLanguageText));
    }

    [RelayCommand]
    private async Task ClearTranslationMemory()
    {
        if (_translationMemory == null) return;
        if (!await _messages.ConfirmAsync("Delete every entry in the translation memory? This cannot be undone.", "Data & Privacy")) return;
        _translationMemory.Clear();
        OnPropertyChanged(nameof(TmEntryCount));
    }

    [RelayCommand]
    private async Task ExportSettings()
    {
        var file = await _dialogService.SaveFileAsync(null, "toucan-settings.json", "Export Settings", [new FileFilter("JSON", "*.json")]);
        if (file == null) return;
        var bundle = new SettingsBundle { App = AppOptions, Defaults = ProjectDefaults };
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(bundle, SettingsBundle.JsonOptions));
        await _messages.ShowMessageAsync($"Settings exported to {file}", "Export Settings");
    }

    [RelayCommand]
    private async Task ImportSettings()
    {
        var file = await _dialogService.SelectFileAsync(null, "Import Settings", [new FileFilter("JSON", "*.json")]);
        if (file == null) return;
        try
        {
            var bundle = JsonSerializer.Deserialize<SettingsBundle>(await File.ReadAllTextAsync(file), SettingsBundle.JsonOptions);
            if (bundle?.App != null) AppOptions = bundle.App;
            if (bundle?.Defaults != null) ProjectDefaults = bundle.Defaults;
            LoadFromOptions();
        }
        catch (JsonException ex)
        {
            await _messages.ShowMessageAsync("Not a Toucan settings file: " + ex.Message, "Import Settings");
        }
    }

    [RelayCommand]
    private async Task ResetAllSettings()
    {
        if (!await _messages.ConfirmAsync("Reset all preferences to their defaults? Click Save to apply.", "Reset Settings")) return;
        var lastProject = AppOptions.LastProjectPath;
        AppOptions = new AppOptions { LastProjectPath = lastProject };
        ProjectDefaults = new ProjectDefaults();
        LoadFromOptions();
    }

    [RelayCommand]
    private static void OpenUrl(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url)) PlatformService.OpenUrl(url);
    }

    [RelayCommand]
    private Task CopyVersion() => PlatformService.SetClipboardTextAsync($"{VersionText} ({RuntimeText})");

    [RelayCommand]
    private static void OpenPluginsFolder()
    {
        var folder = PluginHostOptions.DefaultRoot();
        Directory.CreateDirectory(folder);
        PlatformService.RevealInFileManager(folder);
    }

    [RelayCommand]
    private static void OpenSettingsFolder() =>
        PlatformService.RevealInFileManager(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan"));

    private static List<string> ParseList(string value) =>
        string.IsNullOrWhiteSpace(value) ? [] : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private sealed class SettingsBundle
    {
        public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        public AppOptions? App { get; set; }
        public ProjectDefaults? Defaults { get; set; }
    }
}

/// <summary>Enable/severity toggle for one validation rule in the Options dialog.</summary>
public partial class ValidationRuleOption(string id, string label) : ObservableObject
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string severity = "Warning";
}

/// <summary>A settings sidebar entry: page title, <c>FASymbol</c> name and the tile color behind the icon.</summary>
/// <summary>An interface language: the code that is stored and its native name. <c>ToString</c> is what the picker shows.</summary>
public sealed record AppLanguageOption(string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed record SettingsNavEntry(string Title, string Icon, string Color)
{
    public global::Avalonia.Media.IBrush TileBrush { get; } = global::Avalonia.Media.Brush.Parse(Color);
}

public sealed record ShortcutGroup(string Category, IReadOnlyList<KeybindingEntry> Items);
