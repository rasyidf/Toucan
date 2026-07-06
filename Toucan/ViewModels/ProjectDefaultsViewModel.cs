using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Options;

namespace Toucan.ViewModels;

/// <summary>
/// ViewModel for the "Manage Project Defaults" window.
/// Edits the ProjectDefaults template that seeds new projects and provides fallback values.
/// </summary>
public partial class ProjectDefaultsViewModel : ObservableObject
{
    private readonly IProjectDefaultsService _service;
    private ProjectDefaults _defaults;

    public ProjectDefaultsViewModel(IProjectDefaultsService service)
    {
        _service = service;
        _defaults = _service.Load();
        LoadFromDefaults();
    }

    public Action<bool?>? CloseAction { get; set; }

    // --- Editor Defaults ---
    [ObservableProperty] private string defaultLanguage = "en-US";
    [ObservableProperty] private bool saveEmptyTranslations = true;
    [ObservableProperty] private string translationOrder = "Alphabetically sorted";
    [ObservableProperty] private bool commentsEnabled = true;

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

    // --- Translation Defaults ---
    [ObservableProperty] private string defaultProvider = "Google";
    [ObservableProperty] private string formality = "Default";
    [ObservableProperty] private string context = string.Empty;
    [ObservableProperty] private bool preservePlaceholders = true;
    [ObservableProperty] private bool previewBeforeApply = true;

    // --- Validation Defaults ---
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

    // --- Source Code Defaults ---
    [ObservableProperty] private bool autoScanOnOpen;
    [ObservableProperty] private string scanExtensions = string.Empty;
    [ObservableProperty] private string excludedDirectories = string.Empty;
    [ObservableProperty] private string externalEditor = "code --goto {file}:{line}";

    // --- Feature Defaults ---
    [ObservableProperty] private bool autoSaveEnabled;
    [ObservableProperty] private string autoSaveIntervalText = "60";

    // --- Languages ---
    [ObservableProperty] private string defaultProjectLanguages = "en-US";

    private void LoadFromDefaults()
    {
        var d = _defaults;
        DefaultLanguage = d.DefaultLanguage ?? "en-US";
        SaveEmptyTranslations = d.SaveEmptyTranslations;
        TranslationOrder = d.TranslationOrder == "primary_language" ? "Primary language" : "Alphabetically sorted";
        CommentsEnabled = d.CommentsEnabled;
        CopyTemplates.Clear();
        foreach (var t in d.CopyTemplates ?? ["%1"])
            CopyTemplates.Add(new CopyTemplateItem(t));
        if (CopyTemplates.Count == 0) CopyTemplates.Add(new CopyTemplateItem("%1"));
        OnPropertyChanged(nameof(CanAddCopyTemplate));

        DefaultProvider = d.DefaultProvider ?? "Google";
        Formality = d.Formality ?? "Default";
        Context = d.Context ?? "";
        PreservePlaceholders = d.PreservePlaceholders;
        PreviewBeforeApply = d.PreviewBeforeApply;

        ValidateOnSave = d.ValidateOnSave;
        LoadValidationRules(d.ValidationRules ?? []);

        AutoScanOnOpen = d.AutoScanOnOpen;
        ScanExtensions = string.Join(", ", d.ScanExtensions ?? []);
        ExcludedDirectories = string.Join(", ", d.ExcludedDirectories ?? []);
        ExternalEditor = d.ExternalEditor ?? "code --goto {file}:{line}";

        AutoSaveEnabled = d.AutoSaveEnabled;
        AutoSaveIntervalText = d.AutoSaveIntervalSeconds.ToString();
        DefaultProjectLanguages = string.Join(", ", d.DefaultProjectLanguages ?? ["en-US"]);
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

    [RelayCommand]
    private void Save()
    {
        _defaults.DefaultLanguage = DefaultLanguage;
        _defaults.SaveEmptyTranslations = SaveEmptyTranslations;
        _defaults.TranslationOrder = TranslationOrder == "Primary language" ? "primary_language" : "alphabetical";
        _defaults.CommentsEnabled = CommentsEnabled;
        _defaults.CopyTemplates = [.. CopyTemplates.Select(t => t.Value)];
        _defaults.DefaultProvider = DefaultProvider;
        _defaults.Formality = Formality;
        _defaults.Context = Context;
        _defaults.PreservePlaceholders = PreservePlaceholders;
        _defaults.PreviewBeforeApply = PreviewBeforeApply;
        _defaults.ValidateOnSave = ValidateOnSave;
        _defaults.ValidationRules = new()
        {
            ["missing-translation"] = new(RuleMissingTranslationEnabled, RuleMissingTranslationSeverity),
            ["placeholder-mismatch"] = new(RulePlaceholderMismatchEnabled, RulePlaceholderMismatchSeverity),
            ["duplicate-key"] = new(RuleDuplicateKeyEnabled, RuleDuplicateKeySeverity),
            ["untranslated-copy"] = new(RuleUntranslatedCopyEnabled, RuleUntranslatedCopySeverity),
            ["empty-value"] = new(RuleEmptyValueEnabled, RuleEmptyValueSeverity),
            ["whitespace-mismatch"] = new(RuleWhitespaceMismatchEnabled, RuleWhitespaceMismatchSeverity),
        };
        _defaults.AutoScanOnOpen = AutoScanOnOpen;
        _defaults.ScanExtensions = ParseCsv(ScanExtensions);
        _defaults.ExcludedDirectories = ParseCsv(ExcludedDirectories);
        _defaults.ExternalEditor = ExternalEditor;
        _defaults.AutoSaveEnabled = AutoSaveEnabled;
        if (!int.TryParse(AutoSaveIntervalText, out int interval) || interval < 10) interval = 60;
        _defaults.AutoSaveIntervalSeconds = Math.Clamp(interval, 10, 600);
        _defaults.DefaultProjectLanguages = ParseCsv(DefaultProjectLanguages);
        if (_defaults.DefaultProjectLanguages.Count == 0) _defaults.DefaultProjectLanguages = ["en-US"];

        _service.Save(_defaults);
        CloseAction?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseAction?.Invoke(false);

    [RelayCommand]
    private void ResetToFactoryDefaults()
    {
        _defaults = new ProjectDefaults();
        LoadFromDefaults();
    }

    private static List<string> ParseCsv(string value) =>
        string.IsNullOrWhiteSpace(value) ? [] :
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
