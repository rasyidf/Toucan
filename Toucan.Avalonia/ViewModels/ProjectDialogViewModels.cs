using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Framework tile shown in the New Project grid.</summary>
public sealed class FrameworkTile
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Icon { get; init; } = "🌐";
    /// <summary>Format ID (see <see cref="FormatIds"/>) new projects from this tile use.</summary>
    public string FormatId { get; init; } = FormatIds.Json;

    /// <summary>Framework profile ID (matches IFrameworkProfile.Id). Null = use the FormatId-based fallback.</summary>
    public string? ProfileId { get; init; }

    private static readonly string[] Palette = ["#007AFF", "#34C759", "#FF9500", "#AF52DE", "#FF2D55", "#5AC8FA", "#5856D6", "#FF3B30", "#30B0C7", "#A2845E"];
    private static readonly Dictionary<string, string> Badges = new(StringComparer.Ordinal)
    {
        ["i18next"] = "i18", ["React"] = "Re", ["Vue"] = "Vu", ["Angular"] = "Ng", ["Flutter"] = "Fl", ["Laravel"] = "La", [".NET"] = ".N",
        ["Android"] = "An", ["iOS"] = "iO", ["Ruby/Rails"] = "Rb", ["Svelte"] = "Sv", ["Java"] = "Jv", ["Gettext"] = "PO",
        ["Generic JSON"] = "{}", ["Generic YAML"] = "Ym", ["CSV"] = "Cs",
    };

    /// <summary>Two-letter mark shown on the tile instead of an emoji, so every framework renders in the same style.</summary>
    public string Badge => Badges.TryGetValue(Name, out var b) ? b : Name.Length >= 2 ? Name[..2] : Name;

    public global::Avalonia.Media.IBrush TileBrush =>
        global::Avalonia.Media.Brush.Parse(Palette[(int)((uint)Name.Aggregate(17, (h, c) => unchecked(h * 31 + c)) % (uint)Palette.Length)]);
}

/// <summary>Two-step New Project wizard: template and location, then languages.</summary>
public partial class NewProjectViewModel : ObservableObject
{
    private readonly IProjectService? _projectService;
    private readonly IDialogService? _dialogService;
    private readonly IAsyncMessageService? _messageService;
    private readonly string _defaultBaseFolder;

    public NewProjectViewModel(IProjectService? projectService = null, IDialogService? dialogService = null, IAsyncMessageService? messageService = null, IRecentProjectService? recentProjects = null)
    {
        _projectService = projectService;
        _dialogService = dialogService;
        _messageService = messageService;
        _defaultBaseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan");

        SelectedFramework = Frameworks[0];
        var opts = AppOptions.LoadFromDisk();
        var defaultLang = PreferredLanguageDetector.Resolve(
            opts.DefaultLanguage, opts.DetectLanguageFromRecent, recentProjects?.LoadRecent() ?? []);
        SourceLanguage = defaultLang;
        Languages.Add(defaultLang);
        Languages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsValid));

        ProjectName = "my-project";
        ProjectFolder = Path.Combine(_defaultBaseFolder, ProjectName);
    }

    public ObservableCollection<FrameworkTile> Frameworks { get; } =
    [
        new() { Name = "i18next", Description = "JSON namespaced", Icon = "⚙", FormatId = FormatIds.Namespaced, ProfileId = "i18next" },
        new() { Name = "React", Description = "Flat JSON", Icon = "⚛", FormatId = FormatIds.Json, ProfileId = "generic-json" },
        new() { Name = "Vue", Description = "vue-i18n JSON", Icon = "🟢", FormatId = FormatIds.Json, ProfileId = "generic-json" },
        new() { Name = "Angular", Description = "JSON / XLIFF", Icon = "🅰", FormatId = FormatIds.Json },
        new() { Name = "Flutter", Description = "ARB format", Icon = "🐦", FormatId = FormatIds.Arb },
        new() { Name = "Laravel", Description = "PHP arrays", Icon = "🔷", FormatId = FormatIds.LaravelPhp },
        new() { Name = ".NET", Description = "RESX resource", Icon = "🟣", FormatId = FormatIds.Resx },
        new() { Name = "Android", Description = "strings.xml", Icon = "🤖", FormatId = FormatIds.AndroidXml, ProfileId = "android" },
        new() { Name = "iOS", Description = ".strings", Icon = "🍎", FormatId = FormatIds.IosStrings },
        new() { Name = "Ruby/Rails", Description = "YAML locale", Icon = "💎", FormatId = FormatIds.Yaml },
        new() { Name = "Svelte", Description = "svelte-i18n", Icon = "🔶", FormatId = FormatIds.Json },
        new() { Name = "Java", Description = ".properties", Icon = "☕", FormatId = FormatIds.JavaProperties },
        new() { Name = "Gettext", Description = "PO files", Icon = "📝", FormatId = FormatIds.Po },
        new() { Name = "Generic JSON", Description = "Flat JSON", Icon = "{ }", FormatId = FormatIds.Json, ProfileId = "generic-json" },
        new() { Name = "Generic YAML", Description = "YAML files", Icon = "≡", FormatId = FormatIds.Yaml },
        new() { Name = "CSV", Description = "CSV table", Icon = "📊", FormatId = FormatIds.Csv },
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string projectName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string projectFolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private FrameworkTile? selectedFramework;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep0), nameof(IsStep1))]
    private int wizardStep;

    [ObservableProperty] private string sourceLanguage = "en-US";

    public ObservableCollection<string> Languages { get; } = [];

    public bool IsStep0 => WizardStep == 0;
    public bool IsStep1 => WizardStep == 1;

    public bool IsValid => !string.IsNullOrWhiteSpace(ProjectName) && !string.IsNullOrWhiteSpace(ProjectFolder)
        && Languages.Count > 0 && SelectedFramework != null;

    /// <summary>If set after <see cref="NextStepCommand"/>, the caller should open this existing project instead.</summary>
    public string? OpenExistingPath { get; private set; }

    /// <summary>Raised when the dialog should close; the argument is the dialog result.</summary>
    public Action<bool>? CloseAction { get; set; }

    [RelayCommand]
    private async Task NextStep()
    {
        if (WizardStep != 0 || SelectedFramework == null || string.IsNullOrWhiteSpace(ProjectFolder)) return;

        if (string.IsNullOrWhiteSpace(ProjectName)) ProjectName = SelectedFramework.Name + "-i18n";

        if (File.Exists(Path.Combine(ProjectFolder, "toucan.tproj")) && _messageService != null)
        {
            var choice = await _messageService.ChooseAsync(
                "The folder already contains a Toucan project.\n\nOpen the existing project, or overwrite it with a new one?",
                "Existing Project Found", "Overwrite", "Open Existing");
            if (choice == ChoiceResult.Secondary)
            {
                OpenExistingPath = ProjectFolder;
                CloseAction?.Invoke(true);
                return;
            }
            if (choice != ChoiceResult.Primary) return;
        }

        WizardStep = 1;
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (WizardStep > 0) WizardStep = 0;
    }

    [RelayCommand]
    private async Task AddLanguage()
    {
        if (_dialogService == null) return;
        var result = await _dialogService.ShowLanguagePromptAsync("Add Language", "Search for a language to add.", null);
        if (!string.IsNullOrWhiteSpace(result) && !Languages.Contains(result)) Languages.Add(result);
    }

    [RelayCommand]
    private void AddDefaults()
    {
        foreach (var d in AppOptions.LoadFromDisk().SuggestedLanguages ?? ["en-US"])
        {
            if (!Languages.Contains(d)) Languages.Add(d);
        }
    }

    [RelayCommand]
    private void RemoveLanguage(string? language)
    {
        if (!string.IsNullOrEmpty(language) && Languages.Count > 1) Languages.Remove(language);
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (_dialogService == null) return;
        var selected = await _dialogService.SelectFolderAsync(Directory.Exists(ProjectFolder) ? ProjectFolder : _defaultBaseFolder, "Project Folder");
        if (selected != null) ProjectFolder = selected;
    }

    [RelayCommand]
    private void Create()
    {
        if (!IsValid) return;
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseAction?.Invoke(false);

    partial void OnProjectNameChanged(string value)
    {
        // Keep the folder in sync with the name while it's still under the default base folder.
        if (!string.IsNullOrWhiteSpace(value) &&
            (string.IsNullOrWhiteSpace(ProjectFolder) || ProjectFolder.StartsWith(_defaultBaseFolder, StringComparison.OrdinalIgnoreCase)))
        {
            ProjectFolder = Path.Combine(_defaultBaseFolder, value.Trim().Replace(' ', '-'));
        }
    }

    partial void OnSelectedFrameworkChanged(FrameworkTile? value)
    {
        if (value != null && string.IsNullOrWhiteSpace(ProjectName)) ProjectName = value.Name + "-i18n";
    }

    /// <summary>Creates the project files on disk. Caller opens the folder afterwards.</summary>
    public void CreateProject()
    {
        if (_projectService == null) throw new InvalidOperationException("Project service is not available.");
        if (!IsValid) throw new InvalidOperationException("Project settings are not valid.");

        var formatId = SelectedFramework?.FormatId ?? FormatIds.Json;
        var settings = _projectService.CreateProject(ProjectFolder, Languages, formatId, true, ProjectName);
        if (SelectedFramework?.ProfileId != null)
        {
            settings.Framework = SelectedFramework.ProfileId;
            settings.PrimaryLanguage = SourceLanguage;
            settings.Save();
        }
    }
}

/// <summary>Import an existing folder by auto-detecting the i18n framework and its files.</summary>
public partial class ImportProjectViewModel : ObservableObject
{
    private readonly IEnumerable<IFrameworkProfile> _profiles;
    private readonly IDialogService _dialogService;

    public ImportProjectViewModel(IEnumerable<IFrameworkProfile> profiles, IDialogService dialogService)
    {
        _profiles = profiles;
        _dialogService = dialogService;
        foreach (var p in profiles) AvailableProfiles.Add(p);
    }

    [ObservableProperty] private string folder = string.Empty;
    [ObservableProperty] private IFrameworkProfile? detectedProfile;
    [ObservableProperty] private IFrameworkProfile? selectedProfile;

    public ObservableCollection<IFrameworkProfile> AvailableProfiles { get; } = [];
    public ObservableCollection<DiscoveredFile> DiscoveredFiles { get; } = [];
    public ObservableCollection<string> DetectedLanguages { get; } = [];

    public bool IsValid => !string.IsNullOrWhiteSpace(Folder) && SelectedProfile != null && DiscoveredFiles.Count > 0;

    public string DetectionSummary => DetectedProfile == null
        ? (Directory.Exists(Folder) ? "No known i18n layout detected. Pick a framework manually." : "Choose a folder to scan.")
        : $"Detected {DetectedProfile.DisplayName} · {DiscoveredFiles.Count} file(s) · {DetectedLanguages.Count} language(s)";

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var selected = await _dialogService.SelectFolderAsync(Folder, "Folder to Import");
        if (!string.IsNullOrEmpty(selected)) Folder = selected;
    }

    partial void OnFolderChanged(string value)
    {
        if (Directory.Exists(value)) RunDetection();
    }

    partial void OnSelectedProfileChanged(IFrameworkProfile? value)
    {
        if (value != null && Directory.Exists(Folder)) RefreshDiscoveredFiles(value);
        OnPropertyChanged(nameof(IsValid));
    }

    private void RunDetection()
    {
        DiscoveredFiles.Clear();
        DetectedLanguages.Clear();

        DetectedProfile = _profiles
            .Select(p => (Profile: p, Score: p.DetectionScore(Folder)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Profile)
            .FirstOrDefault();
        SelectedProfile = DetectedProfile;
        if (SelectedProfile != null) RefreshDiscoveredFiles(SelectedProfile);
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(DetectionSummary));
    }

    private void RefreshDiscoveredFiles(IFrameworkProfile profile)
    {
        DiscoveredFiles.Clear();
        DetectedLanguages.Clear();
        foreach (var file in profile.DiscoverFiles(Folder))
        {
            DiscoveredFiles.Add(file);
            if (!DetectedLanguages.Contains(file.Language)) DetectedLanguages.Add(file.Language);
        }
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(DetectionSummary));
    }
}

/// <summary>Per-project settings stored in the toucan.tproj manifest.</summary>
public partial class ProjectPropertiesViewModel : ObservableObject
{
    private readonly ProjectSettings _settings;
    private readonly IDialogService? _dialogs;
    private readonly IProjectDefaultsService? _defaultsService;
    private readonly IEnumerable<string>? _discoveredLanguages;

    public ProjectPropertiesViewModel(ProjectSettings settings, IDialogService? dialogs, IEnumerable<string>? discoveredLanguages = null, IProjectDefaultsService? defaultsService = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dialogs = dialogs;
        _defaultsService = defaultsService;
        _discoveredLanguages = discoveredLanguages;
        LoadFromSettings();
    }

    public static IReadOnlyList<string> FormalityOptions { get; } = ["Default", "More", "Less", "Formal", "Informal"];

    /// <summary>Sidebar pages of the Project Properties dialog, in display order.</summary>
    public static IReadOnlyList<SettingsNavEntry> NavEntries { get; } =
    [
        new("General", "Settings", "#8E8E93"),
        new("Translation", "Character", "#34C759"),
        new("Editor", "Edit", "#007AFF"),
        new("Source code", "CodeHTML", "#5AC8FA"),
        new("Hidden", "View", "#636366"),
    ];

    public static IReadOnlyList<string> TranslationOrderOptions { get; } = ["Alphabetically sorted", "Primary language"];
    public static IReadOnlyList<string> ProviderOptions { get; } = ["", "Google", "DeepL", "Microsoft", "AI", "Custom", "Mock"];

    [ObservableProperty] private string projectName = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string primaryLanguage = "en-US";
    [ObservableProperty] private string context = string.Empty;
    [ObservableProperty] private string defaultProvider = string.Empty;
    [ObservableProperty] private string formality = "Default";
    [ObservableProperty] private bool preservePlaceholders = true;
    [ObservableProperty] private bool previewBeforeApply = true;
    [ObservableProperty] private bool saveEmptyTranslations = true;
    [ObservableProperty] private string translationOrder = "Alphabetically sorted";
    [ObservableProperty] private bool commentsEnabled = true;
    [ObservableProperty] private bool autoSaveEnabled;
    [ObservableProperty] private decimal autoSaveInterval = 60;
    [ObservableProperty] private string sourceRoots = string.Empty;
    [ObservableProperty] private string externalEditor = string.Empty;
    [ObservableProperty] private string scanExtensions = string.Empty;
    [ObservableProperty] private string excludedDirectories = string.Empty;
    [ObservableProperty] private bool autoScanOnOpen;
    [ObservableProperty] private bool validateOnSave = true;

    public string ProjectPath => _settings.ProjectPath;
    public string SaveStyleName => _settings.SaveFormat;

    public ObservableCollection<CopyTemplateItem> CopyTemplates { get; } = [];
    public bool CanAddCopyTemplate => CopyTemplates.Count < 5;
    public ObservableCollection<string> Languages { get; } = [];
    public ObservableCollection<string> HiddenNamespaces { get; } = [];

    /// <summary>True when the user asked to open Manage Languages; the caller opens it after close.</summary>
    public bool ManageLanguagesRequested { get; private set; }

    public Action<bool>? CloseAction { get; set; }

    private void LoadFromSettings()
    {
        ProjectName = _settings.Name ?? string.Empty;
        Description = _settings.Description ?? string.Empty;
        PrimaryLanguage = _settings.PrimaryLanguage ?? "en-US";
        Context = _settings.Context ?? string.Empty;
        DefaultProvider = _settings.DefaultProvider ?? string.Empty;
        Formality = _settings.Formality ?? "Default";
        PreservePlaceholders = _settings.PreservePlaceholders ?? true;
        PreviewBeforeApply = _settings.PreviewBeforeApply ?? true;
        SaveEmptyTranslations = _settings.SaveEmptyTranslations ?? true;
        TranslationOrder = _settings.TranslationOrder == "primary_language" ? "Primary language" : "Alphabetically sorted";
        CommentsEnabled = _settings.CommentsEnabled ?? true;
        AutoSaveEnabled = _settings.AutoSaveEnabled ?? false;
        AutoSaveInterval = _settings.AutoSaveIntervalSeconds ?? 60;
        ExternalEditor = _settings.ExternalEditor ?? string.Empty;
        SourceRoots = string.Join(";", _settings.SourceRoots ?? []);
        ScanExtensions = string.Join(", ", _settings.ScanExtensions ?? []);
        ExcludedDirectories = string.Join(", ", _settings.ExcludedDirectories ?? []);
        AutoScanOnOpen = _settings.AutoScanOnOpen ?? false;
        ValidateOnSave = _settings.ValidateOnSave ?? true;

        CopyTemplates.Clear();
        foreach (var t in _settings.CopyTemplates is { Count: > 0 } list ? list : ["%1"])
            CopyTemplates.Add(new CopyTemplateItem(t));
        OnPropertyChanged(nameof(CanAddCopyTemplate));

        Languages.Clear();
        foreach (var lang in _discoveredLanguages?.ToList() ?? _settings.Languages ?? [])
            Languages.Add(lang);

        HiddenNamespaces.Clear();
        foreach (var ns in _settings.HiddenNamespaces ?? [])
            HiddenNamespaces.Add(ns);
    }

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
    private void RemoveHiddenNamespace(string? ns)
    {
        if (!string.IsNullOrWhiteSpace(ns)) HiddenNamespaces.Remove(ns);
    }

    [RelayCommand]
    private async Task BrowseSourceRoot()
    {
        if (_dialogs == null) return;
        var folder = await _dialogs.SelectFolderAsync(_settings.ProjectPath, "Source Root");
        if (folder == null) return;
        var relative = Path.GetRelativePath(_settings.ProjectPath, folder);
        SourceRoots = string.IsNullOrWhiteSpace(SourceRoots) ? relative : SourceRoots + ";" + relative;
    }

    [RelayCommand]
    private Task OpenProviderSettings() => _dialogs?.ShowProviderSettingsAsync(_settings.ProjectPath) ?? Task.CompletedTask;

    [RelayCommand]
    private void ManageLanguages()
    {
        ManageLanguagesRequested = true;
        Save();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = _defaultsService?.Load() ?? new ProjectDefaults();
        SaveEmptyTranslations = defaults.SaveEmptyTranslations;
        TranslationOrder = defaults.TranslationOrder == "primary_language" ? "Primary language" : "Alphabetically sorted";
        CommentsEnabled = defaults.CommentsEnabled;
        CopyTemplates.Clear();
        foreach (var t in defaults.CopyTemplates is { Count: > 0 } list ? list : ["%1"])
            CopyTemplates.Add(new CopyTemplateItem(t));
        OnPropertyChanged(nameof(CanAddCopyTemplate));
        DefaultProvider = defaults.DefaultProvider ?? string.Empty;
        Formality = defaults.Formality ?? "Default";
        Context = defaults.Context ?? string.Empty;
        PreservePlaceholders = defaults.PreservePlaceholders;
        PreviewBeforeApply = defaults.PreviewBeforeApply;
        ExternalEditor = defaults.ExternalEditor ?? string.Empty;
        ScanExtensions = string.Join(", ", defaults.ScanExtensions ?? []);
        ExcludedDirectories = string.Join(", ", defaults.ExcludedDirectories ?? []);
        AutoScanOnOpen = defaults.AutoScanOnOpen;
        AutoSaveEnabled = defaults.AutoSaveEnabled;
        AutoSaveInterval = defaults.AutoSaveIntervalSeconds;
        ValidateOnSave = defaults.ValidateOnSave;
    }

    [RelayCommand]
    private void Save()
    {
        _settings.Name = ProjectName;
        _settings.Description = Description;
        _settings.PrimaryLanguage = PrimaryLanguage;
        _settings.Context = string.IsNullOrWhiteSpace(Context) ? null : Context;
        _settings.DefaultProvider = string.IsNullOrWhiteSpace(DefaultProvider) ? null : DefaultProvider;
        _settings.Formality = Formality == "Default" ? null : Formality;
        _settings.PreservePlaceholders = PreservePlaceholders;
        _settings.PreviewBeforeApply = PreviewBeforeApply;
        _settings.SaveEmptyTranslations = SaveEmptyTranslations;
        _settings.TranslationOrder = TranslationOrder == "Primary language" ? "primary_language" : "alphabetical";
        _settings.CommentsEnabled = CommentsEnabled;
        _settings.AutoSaveEnabled = AutoSaveEnabled;
        _settings.AutoSaveIntervalSeconds = (int)Math.Clamp(AutoSaveInterval, 10, 600);
        _settings.ExternalEditor = string.IsNullOrWhiteSpace(ExternalEditor) ? null : ExternalEditor;
        _settings.SourceRoots = SplitList(SourceRoots, ';') ?? [];
        _settings.ScanExtensions = SplitList(ScanExtensions, ',');
        _settings.ExcludedDirectories = SplitList(ExcludedDirectories, ',');
        _settings.AutoScanOnOpen = AutoScanOnOpen;
        _settings.ValidateOnSave = ValidateOnSave;
        _settings.CopyTemplates = [.. CopyTemplates.Select(t => t.Value)];
        _settings.HiddenNamespaces = [.. HiddenNamespaces];
        _settings.Save();
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseAction?.Invoke(false);

    private static List<string>? SplitList(string value, char separator) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : [.. value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
