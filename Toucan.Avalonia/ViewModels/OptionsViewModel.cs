using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Options;
using Toucan.Core.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Application preferences (AppOptions) plus the defaults applied to new projects (ProjectDefaults).</summary>
public partial class OptionsViewModel : ObservableObject
{
    private readonly IPreferenceService _preferenceService;
    private readonly IProjectDefaultsService _defaultsService;
    private readonly IDialogService _dialogService;
    private readonly IAsyncMessageService _messages;
    private readonly IRecentProjectService? _recentProjects;
    private readonly ITranslationMemory? _translationMemory;

    public OptionsViewModel(
        IPreferenceService preferenceService,
        IProjectDefaultsService defaultsService,
        IDialogService dialogService,
        IAsyncMessageService messages,
        IRecentProjectService? recentProjects = null,
        ITranslationMemory? translationMemory = null,
        IPluginCatalog? pluginCatalog = null,
        IPluginPolicyStore? pluginPolicy = null)
    {
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

        foreach (var plugin in pluginCatalog?.Plugins ?? [])
            Plugins.Add(new PluginItemViewModel(plugin, pluginPolicy, messages, () => PluginsChanged = true));
    }

    public static IReadOnlyList<string> Pages { get; } =
        ["General", "Appearance", "Editor", "Translation", "Validation", "Translation Memory", "Source Code", "Languages", "Shortcuts", "Integration", "Data & Privacy", "Plugins", "About"];

    /// <summary>Sidebar entries, in the same order as <see cref="Pages"/>: title, icon and tile color.</summary>
    public static IReadOnlyList<SettingsNavEntry> NavEntries { get; } =
    [
        new("General", "Settings", "#8E8E93"),
        new("Appearance", "DarkTheme", "#5856D6"),
        new("Editor", "Edit", "#007AFF"),
        new("Translation", "Character", "#34C759"),
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

    /// <summary>Index of the Plugins page in <see cref="Pages"/>.</summary>
    public const int PluginsPage = 11;

    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];
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
    public static IReadOnlyList<string> AppLanguageOptions { get; } = Locales.Loc.Available;

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
    [ObservableProperty] private decimal maxItems = 5000;
    [ObservableProperty] private decimal truncateSize = 5000;
    [ObservableProperty] private decimal loadingDepth = 1;

    // Appearance
    [ObservableProperty] private string theme = "System";
    [ObservableProperty] private decimal fontSize = 13;

    // Editor
    [ObservableProperty] private bool plainTextKeys;
    [ObservableProperty] private string defaultLanguage = "en-US";
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
        PageSize = opts.PageSize;
        MaxItems = opts.MaxItems;
        TruncateSize = opts.TruncateResultsOver;
        LoadingDepth = opts.LoadingDepth;
        Theme = ThemeOptions.Contains(opts.Theme) ? opts.Theme : "System";
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
        foreach (var (id, label, severity) in ValidationRuleOption.Known)
        {
            var cfg = defs.ValidationRules.GetValueOrDefault(id, new ValidationRuleConfig(true, severity));
            ValidationRules.Add(new ValidationRuleOption(id, label) { Enabled = cfg.Enabled, Severity = cfg.Severity });
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
        AppOptions.AppLanguage = AppLanguage;
        AppOptions.OpenLastProjectOnStartup = OpenLastProjectOnStartup;
        AppOptions.PageSize = (int)Math.Max(1, PageSize);
        AppOptions.MaxItems = (int)Math.Max(1, MaxItems);
        AppOptions.TruncateResultsOver = (int)Math.Max(1, TruncateSize);
        AppOptions.LoadingDepth = (int)Math.Clamp(LoadingDepth, 1, 5);
        AppOptions.Theme = Theme;
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
        if (!await _messages.ConfirmAsync("Remove all projects from the recent list?", "Data & Privacy")) return;
        foreach (var p in _recentProjects.LoadRecent().ToList()) _recentProjects.Remove(p.Path);
        await _messages.ShowMessageAsync("Recent projects cleared.", "Data & Privacy");
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
    public static readonly (string Id, string Label, string DefaultSeverity)[] Known =
    [
        ("missing-translation", "Missing translation", "Warning"),
        ("placeholder-mismatch", "Placeholder mismatch", "Error"),
        ("duplicate-key", "Duplicate key", "Error"),
        ("untranslated-copy", "Untranslated copy of source", "Info"),
        ("empty-value", "Empty value", "Warning"),
        ("whitespace-mismatch", "Leading/trailing whitespace mismatch", "Info"),
    ];

    public string Id { get; } = id;
    public string Label { get; } = label;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string severity = "Warning";
}

/// <summary>A settings sidebar entry: page title, <c>FASymbol</c> name and the tile color behind the icon.</summary>
public sealed record SettingsNavEntry(string Title, string Icon, string Color)
{
    public global::Avalonia.Media.IBrush TileBrush { get; } = global::Avalonia.Media.Brush.Parse(Color);
}

public sealed record ShortcutGroup(string Category, IReadOnlyList<KeybindingEntry> Items);
