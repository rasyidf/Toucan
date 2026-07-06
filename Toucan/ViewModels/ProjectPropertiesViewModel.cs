using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Services;

namespace Toucan.ViewModels;

public partial class ProjectPropertiesViewModel : ObservableObject
{
    private readonly ProjectSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly IProjectDefaultsService? _defaultsService;
    private readonly IEnumerable<string>? _discoveredLanguages;

    // --- Identity ---
    [ObservableProperty] private string projectName = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string primaryLanguage = "en-US";

    // --- Translation ---
    [ObservableProperty] private string context = string.Empty;
    [ObservableProperty] private string defaultProvider = string.Empty;
    [ObservableProperty] private string formality = "Default";
    [ObservableProperty] private bool preservePlaceholders = true;
    [ObservableProperty] private bool previewBeforeApply = true;

    // --- Editor ---
    [ObservableProperty] private bool saveEmptyTranslations = true;
    [ObservableProperty] private string translationOrder = "Alphabetically sorted";

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

    // --- Features ---
    [ObservableProperty] private bool commentsEnabled = true;
    [ObservableProperty] private bool autoSaveEnabled;
    [ObservableProperty] private string autoSaveIntervalText = "60";

    // --- Source Code ---
    [ObservableProperty] private string sourceRoots = string.Empty;
    [ObservableProperty] private string externalEditor = string.Empty;
    [ObservableProperty] private string scanExtensions = string.Empty;
    [ObservableProperty] private string excludedDirectories = string.Empty;
    [ObservableProperty] private bool autoScanOnOpen;

    // --- Validation ---
    [ObservableProperty] private bool validateOnSave = true;

    public ObservableCollection<string> Languages { get; } = [];

    public ObservableCollection<string> HiddenNamespaces { get; } = [];

    public Action<bool?>? CloseAction { get; set; }

    public ProjectPropertiesViewModel(ProjectSettings settings, IDialogService dialogs, IEnumerable<string>? discoveredLanguages = null, IProjectDefaultsService? defaultsService = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dialogs = dialogs;
        _defaultsService = defaultsService;
        _discoveredLanguages = discoveredLanguages;
        LoadFromSettings();
    }

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
        AutoSaveIntervalText = (_settings.AutoSaveIntervalSeconds ?? 60).ToString();
        ExternalEditor = _settings.ExternalEditor ?? string.Empty;
        SourceRoots = string.Join(";", _settings.SourceRoots ?? []);
        ScanExtensions = string.Join(", ", _settings.ScanExtensions ?? []);
        ExcludedDirectories = string.Join(", ", _settings.ExcludedDirectories ?? []);
        AutoScanOnOpen = _settings.AutoScanOnOpen ?? false;
        ValidateOnSave = _settings.ValidateOnSave ?? true;

        if (_settings.CopyTemplates is { Count: > 0 })
        {
            CopyTemplates.Clear();
            foreach (var t in _settings.CopyTemplates)
                CopyTemplates.Add(new CopyTemplateItem(t));
        }
        else
        {
            CopyTemplates.Clear();
            CopyTemplates.Add(new CopyTemplateItem("%1"));
        }
        OnPropertyChanged(nameof(CanAddCopyTemplate));

        Languages.Clear();
        var langs = _discoveredLanguages?.ToList() ?? _settings.Languages ?? [];
        foreach (var lang in langs)
            Languages.Add(lang);

        HiddenNamespaces.Clear();
        foreach (var ns in _settings.HiddenNamespaces ?? [])
            HiddenNamespaces.Add(ns);
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
        _settings.ExternalEditor = string.IsNullOrWhiteSpace(ExternalEditor) ? null : ExternalEditor;
        _settings.SourceRoots = string.IsNullOrWhiteSpace(SourceRoots)
            ? []
            : [.. SourceRoots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        _settings.ScanExtensions = string.IsNullOrWhiteSpace(ScanExtensions)
            ? null
            : [.. ScanExtensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        _settings.ExcludedDirectories = string.IsNullOrWhiteSpace(ExcludedDirectories)
            ? null
            : [.. ExcludedDirectories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        _settings.AutoScanOnOpen = AutoScanOnOpen;
        _settings.ValidateOnSave = ValidateOnSave;

        if (!int.TryParse(AutoSaveIntervalText, out int interval) || interval < 10)
            interval = 60;
        _settings.AutoSaveIntervalSeconds = Math.Clamp(interval, 10, 600);

        _settings.CopyTemplates = [.. CopyTemplates.Select(t => t.Value)];

        _settings.HiddenNamespaces = [.. HiddenNamespaces];

        _settings.Save();
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseAction?.Invoke(false);
    }

    [RelayCommand]
    private void OpenProviderSettings()
    {
        _dialogs?.ShowProviderSettings();
    }

    [RelayCommand]
    private void ManageLanguages()
    {
        // Close dialog — caller will detect ManageLanguagesRequested and open the manage languages dialog
        ManageLanguagesRequested = true;
        CloseAction?.Invoke(true);
    }

    /// <summary>Set to true when the user clicks Manage Languages — caller should open the dialog after close.</summary>
    public bool ManageLanguagesRequested { get; private set; }

    [RelayCommand]
    private void RemoveHiddenNamespace(string? ns)
    {
        if (!string.IsNullOrWhiteSpace(ns)) HiddenNamespaces.Remove(ns);
    }

    /// <summary>
    /// Resets all project overrides to null, so the project inherits from ProjectDefaults.
    /// </summary>
    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = _defaultsService?.Load() ?? new ProjectDefaults();

        // Editor
        SaveEmptyTranslations = defaults.SaveEmptyTranslations;
        TranslationOrder = defaults.TranslationOrder == "primary_language" ? "Primary language" : "Alphabetically sorted";
        CommentsEnabled = defaults.CommentsEnabled;
        CopyTemplates.Clear();
        foreach (var t in defaults.CopyTemplates ?? ["%1"])
            CopyTemplates.Add(new CopyTemplateItem(t));
        if (CopyTemplates.Count == 0) CopyTemplates.Add(new CopyTemplateItem("%1"));
        OnPropertyChanged(nameof(CanAddCopyTemplate));

        // Translation
        DefaultProvider = defaults.DefaultProvider ?? "Google";
        Formality = defaults.Formality ?? "Default";
        Context = defaults.Context ?? "";
        PreservePlaceholders = defaults.PreservePlaceholders;
        PreviewBeforeApply = defaults.PreviewBeforeApply;

        // Source Code
        ExternalEditor = defaults.ExternalEditor ?? "";
        ScanExtensions = string.Join(", ", defaults.ScanExtensions ?? []);
        ExcludedDirectories = string.Join(", ", defaults.ExcludedDirectories ?? []);
        AutoScanOnOpen = defaults.AutoScanOnOpen;

        // Features
        AutoSaveEnabled = defaults.AutoSaveEnabled;
        AutoSaveIntervalText = defaults.AutoSaveIntervalSeconds.ToString();
        ValidateOnSave = defaults.ValidateOnSave;
    }
}
