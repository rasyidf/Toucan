using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Models;
using Toucan.Services;

namespace Toucan.ViewModels;

public partial class ProjectPropertiesViewModel : ObservableObject
{
    private readonly ProjectSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly IEnumerable<string>? _discoveredLanguages;

    // --- Identity ---
    [ObservableProperty] private string projectName = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string primaryLanguage = "en-US";

    // --- Translation ---
    [ObservableProperty] private string context = string.Empty;
    [ObservableProperty] private string defaultProvider = string.Empty;
    [ObservableProperty] private string formality = "Default";

    // --- Editor ---
    [ObservableProperty] private bool saveEmptyTranslations = true;
    [ObservableProperty] private string translationOrder = "Alphabetically sorted";
    [ObservableProperty] private string copyTemplate1 = "%1";
    [ObservableProperty] private string copyTemplate2 = string.Empty;
    [ObservableProperty] private string copyTemplate3 = string.Empty;

    // --- Features ---
    [ObservableProperty] private bool commentsEnabled = true;
    [ObservableProperty] private bool autoSaveEnabled;
    [ObservableProperty] private string autoSaveIntervalText = "60";

    // --- Source Code ---
    [ObservableProperty] private string sourceRoots = string.Empty;
    [ObservableProperty] private string externalEditor = string.Empty;

    public ObservableCollection<string> Languages { get; } = [];

    public ObservableCollection<string> HiddenNamespaces { get; } = [];

    public Action<bool?>? CloseAction { get; set; }

    public ProjectPropertiesViewModel(ProjectSettings settings, IDialogService dialogs, IEnumerable<string>? discoveredLanguages = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dialogs = dialogs;
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
        SaveEmptyTranslations = _settings.SaveEmptyTranslations;
        TranslationOrder = _settings.TranslationOrder == "primary_language" ? "Primary language" : "Alphabetically sorted";
        CommentsEnabled = _settings.CommentsEnabled;
        AutoSaveEnabled = _settings.AutoSaveEnabled;
        AutoSaveIntervalText = _settings.AutoSaveIntervalSeconds.ToString();
        ExternalEditor = _settings.ExternalEditor ?? string.Empty;
        SourceRoots = string.Join(";", _settings.SourceRoots ?? []);

        if (_settings.CopyTemplates is { Count: > 0 })
        {
            CopyTemplate1 = _settings.CopyTemplates.Count > 0 ? _settings.CopyTemplates[0] : "%1";
            CopyTemplate2 = _settings.CopyTemplates.Count > 1 ? _settings.CopyTemplates[1] : string.Empty;
            CopyTemplate3 = _settings.CopyTemplates.Count > 2 ? _settings.CopyTemplates[2] : string.Empty;
        }

        Languages.Clear();
        // Use discovered languages (from actual loaded files) if available, otherwise fall back to settings
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
        _settings.SaveEmptyTranslations = SaveEmptyTranslations;
        _settings.TranslationOrder = TranslationOrder == "Primary language" ? "primary_language" : "alphabetical";
        _settings.CommentsEnabled = CommentsEnabled;
        _settings.AutoSaveEnabled = AutoSaveEnabled;
        _settings.ExternalEditor = string.IsNullOrWhiteSpace(ExternalEditor) ? null : ExternalEditor;
        _settings.SourceRoots = string.IsNullOrWhiteSpace(SourceRoots)
            ? []
            : [.. SourceRoots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        if (!int.TryParse(AutoSaveIntervalText, out int interval) || interval < 10)
            interval = 60;
        _settings.AutoSaveIntervalSeconds = Math.Clamp(interval, 10, 600);

        _settings.CopyTemplates = [CopyTemplate1 ?? "%1", CopyTemplate2 ?? "", CopyTemplate3 ?? ""];

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
        // ponytail: full language management requires the loaded translation items (available from main window).
        // This button is a convenience shortcut — for now it's a stub.
    }

    [RelayCommand]
    private void RemoveHiddenNamespace(string? ns)
    {
        if (!string.IsNullOrWhiteSpace(ns)) HiddenNamespaces.Remove(ns);
    }
}
