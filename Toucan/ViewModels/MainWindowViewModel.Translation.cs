using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Extensions;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Extensions;
using Toucan.Locales;
using Toucan.Services;
using Toucan.Views.Dialogs;

namespace Toucan.ViewModels;

/// <summary>
/// Translation operations: pre-translate, provider options, validation, analysis, bulk actions, translation memory/suggestions.
/// </summary>
internal partial class MainWindowViewModel
{
    /// <summary>
    /// Notifies the translation management service about bulk value changes.
    /// Called after bulk operations (pre-translate, approve, etc.) to update dirty tracking.
    /// Falls back to setting IsDirty directly if the service is unavailable.
    /// </summary>
    private void NotifyBulkValueChanges(IEnumerable<TranslationItem> items)
    {
        if (_translationManagement != null)
        {
            foreach (var item in items)
            {
                _translationManagement.NotifyValueChanged(item, item.Value);
                if (!string.IsNullOrEmpty(item.Namespace))
                    SessionDirtyKeys.Add(item.Namespace);
            }
        }
        else
        {
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.Namespace))
                    SessionDirtyKeys.Add(item.Namespace);
            }
            IsDirty = true;
        }
        SessionDirtyCount = SessionDirtyKeys.Count;
        MarkDirtyGroups();
    }

    #region Pre-Translation & Bulk Actions

    [RelayCommand]
    private async Task PreTranslateBulk()
    {
        try
        {
            if (AllTranslation == null || AllTranslation.Count == 0)
            {
                _messageService.ShowMessage(Strings.Status_NoTranslationsLoaded);
                return;
            }

            // If we have a UI-capable pretranslation engine, show the Pre-Translate dialog instead
            if (_pretranslationService != null)
            {
                // gather language list
                List<string> languages = AllTranslation.ToLanguages().ToList();

                var vm = _preTranslateFactory != null ? _preTranslateFactory(languages, AllTranslation, _pretranslationService) : new PreTranslateViewModel(languages, AllTranslation, _pretranslationService, _dialogService, _providerSettingsService);
                bool result = _dialogService.ShowPreTranslate(vm);

                if (result)
                {
                    UpdateSummaryInfo();
                    NotifyBulkValueChanges(AllTranslation);
                    StatusText = Strings.Status_PreTranslateComplete;
                }

                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            // Ensure continuation runs on the UI context so we can update UI-bound properties safely
            await _bulkActionService.PreTranslateAsync(AllTranslation).ConfigureAwait(true);
            UpdateSummaryInfo();
            NotifyBulkValueChanges(AllTranslation);
            StatusText = Strings.Status_PreTranslateComplete;
        }
        catch (Exception ex)
        {
            // Catch any unexpected exception in the UI flow and surface a friendly message instead of crashing
            try
            {
                _messageService.ShowMessage("Error during pre-translation: " + ex.Message);
            }
            catch
            {
                // if message service fails, swallow — no direct UI calls
            }
        }
    }

    [RelayCommand]
    private void GenerateStatisticsBulk()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded to generate statistics.");
            return;
        }

        var filtered = AllTranslation.Where(t => !IsNamespaceHidden(t.Namespace)).ToList();
        StatisticsDialog dialog = new(filtered, System.Windows.Application.Current?.MainWindow);
        _ = dialog.ShowDialog();
    }

    // Per-language / contextual pre-translate and stats commands
    [RelayCommand]
    private async Task PreTranslateLanguage(SummaryItem item)
    {
        try
        {
            IsLoading = true;

            if (item == null)
            {
                return;
            }

            if (AllTranslation == null || AllTranslation.Count == 0)
            {
                _messageService.ShowMessage("No translations loaded to pre-translate.");
                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            List<TranslationItem> toTranslate = AllTranslation.Where(t => t.Language == item.Language).ToList();
            await _bulkActionService.PreTranslateAsync(toTranslate).ConfigureAwait(true);
            UpdateSummaryInfo();
            NotifyBulkValueChanges(toTranslate);
            StatusText = $"Pre-translation completed for {item.Language}.";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage("Error during pre-translation: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PreTranslateNamespace(SummaryItem item)
    {
        try
        {
            IsLoading = true;

            if (item == null)
            {
                return;
            }

            var ns = _dialogService.ShowPrompt("Pre-translate namespace", "Enter namespace (exact or prefix) to pre-translate for language: " + item.Language, "");
            if (ns == null)
            {
                return;
            }

            ns = ns.Trim();
            if (string.IsNullOrWhiteSpace(ns))
            {
                return;
            }

            List<TranslationItem> toTranslate = AllTranslation.Where(t => t.Language == item.Language && t.Namespace != null && (t.Namespace == ns || t.Namespace.StartsWith(ns))).ToList();

            if (toTranslate.Count == 0)
            {
                _messageService.ShowMessage("No translations matched the namespace.");
                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            await _bulkActionService.PreTranslateAsync(toTranslate).ConfigureAwait(true);
            UpdateSummaryInfo();
            NotifyBulkValueChanges(toTranslate);
            StatusText = $"Pre-translation completed for {item.Language} (namespace: {ns}).";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage("Error during pre-translation: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PreTranslateKey(SummaryItem item)
    {
        try
        {
            IsLoading = true;

            if (item == null)
            {
                return;
            }

            var key = _dialogService.ShowPrompt("Pre-translate key", "Enter exact key namespace/id to pre-translate for language: " + item.Language, "");
            if (key == null)
            {
                return;
            }

            key = key.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            List<TranslationItem> toTranslate = AllTranslation.Where(t => t.Language == item.Language && t.Namespace == key).ToList();

            if (toTranslate.Count == 0)
            {
                _messageService.ShowMessage("No matching key found for that language.");
                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            await _bulkActionService.PreTranslateAsync(toTranslate).ConfigureAwait(true);
            UpdateSummaryInfo();
            NotifyBulkValueChanges(toTranslate);
            StatusText = $"Pre-translation completed for {item.Language} (key: {key}).";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage("Error during pre-translation: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void GenerateStatisticsForLanguage(SummaryItem item)
    {
        try
        {
            IsLoading = true;
            if (item == null)
            {
                return;
            }

            if (AllTranslation == null || AllTranslation.Count == 0)
            {
                _messageService.ShowMessage("No translations loaded to generate statistics.");
                return;
            }

            if (_bulkActionService == null)
            {
                _messageService.ShowMessage("Bulk action service is not available.");
                return;
            }

            var langItems = AllTranslation.Where(t => t.Language == item.Language);
            var stats = _bulkActionService.GenerateStatistics(langItems);
            StatusText = stats;
            _messageService.ShowMessage(stats);
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage("Error generating statistics: " + ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void HideLanguage(SummaryItem item)
    {
        if (item == null)
        {
            return;
        }

        // For now 'hide' removes the summary entry from the panel. This is a UI-level action.
        _ = SummaryInfo.Details.Remove(item);
    }

    [RelayCommand]
    private void ApproveAllForLanguage(SummaryItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Language))
        {
            return;
        }

        var languageItems = AllTranslation
            .Where(t => t.Language == item.Language && !string.IsNullOrEmpty(t.Value) && !t.IsApproved)
            .ToList();

        if (languageItems.Count == 0)
        {
            _messageService.ShowMessage("No items to approve.");
            return;
        }

        if (!_messageService.ShowConfirmation($"Approve {languageItems.Count} translated item(s) for '{item.Language}'?"))
        {
            return;
        }

        foreach (var t in languageItems)
        {
            t.IsApproved = true;
        }

        UpdateSummaryInfo();
        // Approval changes need persistence but aren't tracked by value/comment baselines.
        // Notify the service about the items so dirty state is raised if not already dirty.
        NotifyBulkValueChanges(languageItems);
        StatusText = $"Approved {languageItems.Count} item(s) for {item.Language}.";
    }

    #endregion

    #region Source Code & Analysis

    // --- Source Code Integration ---

    [ObservableProperty]
    private bool sourceCodeScanned;

    [ObservableProperty]
    private string sourceCodeStatus = string.Empty;

    /// <summary>All key usages found during the last source scan, exposed for the SourceCodePanel.</summary>
    public ObservableCollection<KeyUsage> SourceCodeUsages { get; } = [];

    [RelayCommand]
    private async Task ScanSourceCode()
    {
        if (_sourceCodeService == null || string.IsNullOrEmpty(CurrentPath))
        {
            return;
        }

        ProjectSettings? settings = ProjectSettings.LoadFrom(CurrentPath);
        var roots = settings?.SourceRoots ?? [];
        // Default: scan project folder itself if no source roots configured
        var scanPath = roots.Count > 0
            ? Path.Combine(CurrentPath, roots[0])
            : CurrentPath;

        StatusText = "Scanning source code...";
        var result = await _sourceCodeService.ScanAsync(scanPath).ConfigureAwait(true);
        SourceCodeScanned = true;
        SourceCodeStatus = $"{result.KeysFound} keys found in {result.FilesScanned} files ({result.Duration.TotalSeconds:F1}s)";
        StatusText = SourceCodeStatus;

        // Populate the usages collection for the panel
        SourceCodeUsages.Clear();
        foreach (var key in _sourceCodeService.GetAllKeys())
        {
            foreach (var usage in _sourceCodeService.FindUsages(key))
            {
                SourceCodeUsages.Add(usage);
            }
        }
    }

    [RelayCommand]
    private async Task SelectSourceRoot()
    {
        var selected = _dialogService.SelectFolder(CurrentPath);
        if (string.IsNullOrEmpty(selected)) return;

        var settings = ProjectSettings.LoadFrom(CurrentPath) ?? ProjectSettings.CreateDefault(CurrentPath);
        // Store relative path if inside project, otherwise absolute
        var relative = Path.GetRelativePath(CurrentPath, selected);
        settings.SourceRoots = [relative];
        settings.Save();

        await ScanSourceCode().ConfigureAwait(true);
    }

    [RelayCommand]
    private void FilterUsedKeys()
    {
        if (_sourceCodeService == null || !_sourceCodeService.HasScanData || AllTranslation == null)
        {
            return;
        }

        FilteredBySourceUsage = "used";
        OnPropertyChanged(nameof(FilteredBySourceUsage));
        Search(SearchText ?? "", true);
    }

    [RelayCommand]
    private void FilterUnusedKeys()
    {
        if (_sourceCodeService == null || !_sourceCodeService.HasScanData || AllTranslation == null)
        {
            return;
        }

        FilteredBySourceUsage = "unused";
        OnPropertyChanged(nameof(FilteredBySourceUsage));
        Search(SearchText ?? "", true);
    }

    [RelayCommand]
    private void ClearSourceFilter()
    {
        FilteredBySourceUsage = null;
        OnPropertyChanged(nameof(FilteredBySourceUsage));
        Search(SearchText ?? "", true);
    }

    [RelayCommand]
    private void OpenInEditor(string key)
    {
        if (_sourceCodeService == null)
        {
            return;
        }

        List<KeyUsage> usages = _sourceCodeService.FindUsages(key).ToList();
        if (usages.Count == 0)
        {
            return;
        }

        var first = usages[0];
        ProjectSettings? settings = ProjectSettings.LoadFrom(CurrentPath);
        var editor = settings?.ExternalEditor ?? "code --goto \"{file}:{line}\"";
        var fullPath = Path.Combine(CurrentPath, first.FilePath);

        var cmd = editor.Replace("{file}", fullPath).Replace("{line}", first.Line.ToString());
        try { _ = Process.Start(new ProcessStartInfo("cmd", $"/c {cmd}") { CreateNoWindow = true }); }
        catch { /* non-critical */ }
    }

    /// <summary>Current source usage filter: null = off, "used", "unused".</summary>
    public string? FilteredBySourceUsage { get; private set; }

    /// <summary>Get source code usages for the selected key.</summary>
    public IEnumerable<KeyUsage> GetKeyUsages(string key)
    {
        return _sourceCodeService?.FindUsages(key) ?? [];
    }

    // --- Translation Quality Analysis ---

    public ObservableCollection<AnalysisResult> AnalysisResults { get; } = [];

    [RelayCommand]
    private async Task AnalyzeTranslations()
    {
        if (_translationAnalyzer == null || AllTranslation == null || AllTranslation.Count == 0)
        {
            return;
        }

        ProjectSettings? settings = ProjectSettings.LoadFrom(CurrentPath);
        var primaryLang = settings?.PrimaryLanguage ?? "en-US";
        var appContext = AppOptions.Context;

        if (string.IsNullOrWhiteSpace(appContext))
        {
            appContext = _dialogService.ShowPrompt("Application Context",
                "Describe your application domain (e.g., 'Banking app for managing savings accounts and transactions').\nThis helps the analyzer check domain-specific terminology.",
                appContext ?? "");
            if (string.IsNullOrWhiteSpace(appContext))
            {
                return;
            }

            AppOptions.Context = appContext;
            _preferenceService.Save(AppOptions);
        }

        // Build analysis items: pair source + each translation
        Dictionary<string, string> sourceMap = AllTranslation
            .Where(i => i.Language == primaryLang && !string.IsNullOrEmpty(i.Value))
            .ToDictionary(i => i.Namespace, i => i.Value);

        List<AnalysisItem> items = AllTranslation
            .Where(i => i.Language != primaryLang && !string.IsNullOrEmpty(i.Value))
            .Where(i => sourceMap.ContainsKey(i.Namespace))
            .Select(i => new AnalysisItem(i.Namespace, sourceMap[i.Namespace], i.Value, i.Language))
            .ToList();

        if (items.Count == 0) { _messageService.ShowMessage("No translations to analyze."); return; }

        // Build provider options from saved settings
        Dictionary<string, string> providerOpts = new();
        if (_providerSettingsService != null)
        {
            var all = _providerSettingsService.LoadAppProviderSettings();
            var match = all.FirstOrDefault(p => string.Equals(p.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                foreach (var kv in match.Options)
                {
                    providerOpts[kv.Key] = kv.Value;
                }

                foreach (var kv in match.Secrets)
                {
                    providerOpts[kv.Key] = kv.Value;
                }
            }
        }

        StatusText = "Analyzing translations...";
        AnalysisRequest request = new()
        {
            Items = items,
            ApplicationContext = appContext,
            SourceLanguage = primaryLang,
            ProviderOptions = providerOpts
        };

        Progress<PretranslationProgress> progress = new(p => StatusText = p.Message ?? $"Analyzing {p.Completed}/{p.Total}...");
        var results = await _translationAnalyzer.AnalyzeAsync(request, progress).ConfigureAwait(true);

        AnalysisResults.Clear();
        foreach (var r in results.Where(r => r.Confidence >= 0.6))
        {
            AnalysisResults.Add(r);
        }

        StatusText = AnalysisResults.Count > 0
            ? $"Analysis complete: {AnalysisResults.Count} issue(s) found"
            : "Analysis complete: no issues found";
    }

    #endregion

    #region Translation Suggestions

    [ObservableProperty]
    private bool suggestionsVisible;

    [ObservableProperty]
    private ObservableCollection<string> suggestions = [];

    [RelayCommand]
    private void ToggleSuggestions()
    {
        SuggestionsVisible = !SuggestionsVisible;
        if (SuggestionsVisible)
        {
            RefreshSuggestions();
        }
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            return;
        }

        // Use SelectedGroup (translation list selection) or fall back to SelectedNode (tree selection)
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (string.IsNullOrEmpty(ns))
        {
            return;
        }

        // ponytail: naive fuzzy match — find translations with similar namespace or value
        var current = AllTranslation.FirstOrDefault(t => t.Namespace == ns);
        if (current == null || string.IsNullOrWhiteSpace(current.Value))
        {
            return;
        }

        var similar = AllTranslation
            .Where(t => t.Language == current.Language && t.Namespace != current.Namespace && !string.IsNullOrWhiteSpace(t.Value))
            .Where(t => t.Value.Contains(current.Value[..Math.Min(4, current.Value.Length)], StringComparison.OrdinalIgnoreCase)
                     || current.Value.Contains(t.Value[..Math.Min(4, t.Value.Length)], StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .Select(t => $"{t.Namespace}: {t.Value}");

        foreach (var s in similar)
        {
            Suggestions.Add(s);
        }
    }

    #endregion

    #region Selected Key Details (Inspector Details tab)

    /// <summary>Namespace/ID of the currently selected key.</summary>
    [ObservableProperty]
    private string selectedKeyNamespace = string.Empty;

    /// <summary>Comment on the currently selected key (from the primary language item).</summary>
    [ObservableProperty]
    private string selectedKeyComment = string.Empty;

    /// <summary>Languages with translations for the selected key and their values.</summary>
    [ObservableProperty]
    private ObservableCollection<string> selectedKeyLanguages = [];

    /// <summary>Audit info string for the selected key.</summary>
    [ObservableProperty]
    private string selectedKeyAuditInfo = string.Empty;

    /// <summary>True when a key is selected and detail data is available.</summary>
    [ObservableProperty]
    private bool hasSelectedKeyDetails;

    private void RefreshSelectedKeyDetails()
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (string.IsNullOrEmpty(ns) || AllTranslation == null || AllTranslation.Count == 0)
        {
            HasSelectedKeyDetails = false;
            SelectedKeyNamespace = string.Empty;
            SelectedKeyComment = string.Empty;
            SelectedKeyLanguages.Clear();
            SelectedKeyAuditInfo = string.Empty;
            return;
        }

        HasSelectedKeyDetails = true;
        SelectedKeyNamespace = ns;

        var items = AllTranslation.Where(t => t.Namespace == ns).ToList();
        var primary = items.FirstOrDefault();
        SelectedKeyComment = primary?.Comment ?? string.Empty;

        SelectedKeyLanguages.Clear();
        foreach (var item in items.OrderBy(t => t.Language))
        {
            var status = string.IsNullOrEmpty(item.Value) ? "⚠ empty" : (item.IsApproved ? "✓ approved" : "● translated");
            SelectedKeyLanguages.Add($"{item.Language}: {status}");
        }

        // Audit info from the first item with metadata
        var audited = items.FirstOrDefault(t => t.LastModifiedUtc.HasValue);
        if (audited != null)
        {
            SelectedKeyAuditInfo = $"Last modified: {audited.LastModifiedUtc:g}\nChange type: {audited.ChangeType}";
            if (audited.ApprovedAtUtc.HasValue)
            {
                SelectedKeyAuditInfo += $"\nApproved: {audited.ApprovedAtUtc:g}";
            }
        }
        else
        {
            SelectedKeyAuditInfo = "No audit data";
        }
    }

    #endregion

    #region Validation (Placeholders, Plural, Gender)

    public ObservableCollection<ValidationIssueItem> ValidationIssues { get; } = [];
    public bool HasValidationIssues => ValidationIssues.Count > 0;

    [RelayCommand]
    private void RunValidation() => ValidatePlaceholders();

    [RelayCommand]
    private void ValidatePlaceholders()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            return;
        }

        ValidationIssues.Clear();

        var settings = Core.Models.ProjectSettings.LoadFrom(CurrentPath)
            ?? Core.Models.ProjectSettings.CreateDefault(CurrentPath);

        // ponytail: resolve actual primary language from loaded items.
        // Settings may say "en-US" but files use "en". Match by prefix if exact not found.
        var actualLangs = AllTranslation.Select(t => t.Language).Distinct().ToList();
        if (!actualLangs.Contains(settings.PrimaryLanguage))
        {
            var match = actualLangs.FirstOrDefault(l =>
                l.StartsWith(settings.PrimaryLanguage.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            if (match != null) settings.PrimaryLanguage = match;
        }

        if (_validationPipeline is Core.Services.Validation.ValidationPipeline pipeline)
        {
            var validationContext = new ValidationContext
            {
                Items = AllTranslation,
                Settings = settings
            };

            var results = pipeline.RunAll(validationContext);
            foreach (var r in results)
            {
                ValidationIssues.Add(new ValidationIssueItem(r.Namespace ?? r.RuleId, $"[{r.Language}] {r.Message}", r.Severity, r.Namespace, r.Language, r.SuggestedFix));
            }
        }
        else
        {
            // Fallback: legacy placeholder-only validation
            List<string> languages = AllTranslation.ToLanguages().ToList();
            var primary = languages.FirstOrDefault() ?? "en";

            List<TranslationItem> sourceItems = AllTranslation.Where(t => t.Language == primary && !string.IsNullOrEmpty(t.Value)).ToList();
            foreach (var source in sourceItems)
            {
                foreach (var lang in languages.Where(l => l != primary))
                {
                    var target = AllTranslation.FirstOrDefault(t => t.Namespace == source.Namespace && t.Language == lang);
                    if (target == null || string.IsNullOrEmpty(target.Value)) continue;

                    var result = PlaceholderService.Validate(source.Value, target.Value);
                    if (!result.IsValid)
                    {
                        var msg = $"[{lang}] missing={string.Join(",", result.Missing)} extra={string.Join(",", result.Extra)}";
                        ValidationIssues.Add(new ValidationIssueItem(source.Namespace, msg));
                    }
                }
            }
        }

        OnPropertyChanged(nameof(HasValidationIssues));

        // Activate Issues panel
        Toucan.Core.Services.SidePanelRegistry.Instance.Activate("issues");

        if (ValidationIssues.Count == 0)
        {
            _messageService.ShowMessage("All validations passed.");
        }
    }

    // TODO: ponytail: real-time on-type validation — hook into value change events and run pipeline per-item. Deferred to v1.1.

    [RelayCommand]
    private void ApplyFix(ValidationIssueItem issue)
    {
        if (issue.SuggestedFix == null || issue.Namespace == null || AllTranslation == null) return;

        var target = AllTranslation.FirstOrDefault(t => t.Namespace == issue.Namespace && t.Language == issue.Language);
        if (target == null) return;

        target.Value = issue.SuggestedFix;
        ValidationIssues.Remove(issue);
        OnPropertyChanged(nameof(HasValidationIssues));
    }

    [RelayCommand]
    private void GeneratePluralForms()
    {
        if (SelectedNode == null || AllTranslation == null)
        {
            return;
        }

        var baseKey = PluralService.GetBaseKey(SelectedNode.Namespace);
        List<string> languages = AllTranslation.ToLanguages().ToList();
        List<TranslationItem> allAdded = [];
        foreach (var lang in languages)
        {
            var missing = PluralService.GenerateMissingForms(baseKey, lang, AllTranslation);
            AllTranslation.AddRange(missing);
            allAdded.AddRange(missing);
        }
        if (allAdded.Count > 0)
        {
            RefreshTree();
            UpdateSummaryInfo();
            _translationManagement?.AddItems(allAdded);
            IsDirty = true;
        }
        StatusText = allAdded.Count > 0 ? $"Generated {allAdded.Count} plural forms for '{baseKey}'" : "All plural forms already exist.";
    }

    [RelayCommand]
    private void GenerateGenderForms()
    {
        if (SelectedNode == null || AllTranslation == null)
        {
            return;
        }

        var baseKey = GenderService.GetBaseKey(SelectedNode.Namespace);
        List<string> languages = AllTranslation.ToLanguages().ToList();
        List<TranslationItem> allAdded = [];
        foreach (var lang in languages)
        {
            var missing = GenderService.GenerateMissingForms(baseKey, lang, AllTranslation);
            AllTranslation.AddRange(missing);
            allAdded.AddRange(missing);
        }
        if (allAdded.Count > 0)
        {
            RefreshTree();
            UpdateSummaryInfo();
            _translationManagement?.AddItems(allAdded);
            IsDirty = true;
        }
        StatusText = allAdded.Count > 0 ? $"Generated {allAdded.Count} gender forms for '{baseKey}'" : "All gender forms already exist.";
    }

    #endregion

    #region Machine Translation Visibility

    [ObservableProperty]
    private bool showMachineTranslations;

    [RelayCommand]
    private void ToggleShowMachineTranslations()
    {
        ShowMachineTranslations = !ShowMachineTranslations;

        if (ShowMachineTranslations)
        {
            if (AllTranslation == null || AllTranslation.Count == 0)
            {
                StatusText = "No translations loaded.";
                return;
            }

            List<TranslationItem> matched = AllTranslation
                .Where(t => t.ChangeType == ChangeType.Suggestion)
                .ToList();

            FilterAndDisplay(matched, "No machine-translated items found.");
            StatusText = matched.Count > 0
                ? $"Showing {matched.Count} machine-translated items"
                : "No machine-translated items found.";
        }
        else
        {
            Search("", true);
            StatusText = "Showing all translations";
        }
    }

    #endregion

    #region Translation Memory Management

    private ITranslationMemory? _translationMemory;

    /// <summary>
    /// Ghost text suggestion from Translation Memory. Populated when the user types in a translation field.
    /// ponytail: UI binding for ghost text overlay goes in the editor DataTemplate (TextBox adorner layer).
    /// The XAML side would bind a semi-transparent TextBlock behind the TextBox to this property.
    /// </summary>
    [ObservableProperty]
    private string? ghostSuggestion;

    /// <summary>TM similarity threshold (0.0–1.0). Matches below this are hidden.</summary>
    public double TmSimilarityThreshold
    {
        get => AppOptions?.TmSimilarityThreshold ?? 0.7;
        set
        {
            if (AppOptions != null && Math.Abs(AppOptions.TmSimilarityThreshold - value) > 0.001)
            {
                AppOptions.TmSimilarityThreshold = value;
                AppOptions.ToDisk();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>True = search all projects' TM entries; false = current project only.</summary>
    public bool TmGlobalScope
    {
        get => AppOptions?.TmGlobalScope ?? true;
        set
        {
            if (AppOptions != null && AppOptions.TmGlobalScope != value)
            {
                AppOptions.TmGlobalScope = value;
                AppOptions.ToDisk();
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Call when user types in a translation field to update GhostSuggestion.
    /// Uses TM fuzzy search with the configured threshold.
    /// </summary>
    internal void UpdateGhostSuggestion(string sourceText, string sourceLanguage, string targetLanguage)
    {
        if (_translationMemory == null || string.IsNullOrWhiteSpace(sourceText))
        {
            GhostSuggestion = null;
            return;
        }

        var threshold = AppOptions?.TmSimilarityThreshold ?? 0.7;
        var match = _translationMemory.Search(sourceText, sourceLanguage, targetLanguage, threshold, 1).FirstOrDefault();
        GhostSuggestion = match?.TargetText;
    }

    [RelayCommand]
    private void ImportTmx()
    {
        var file = _dialogService.SelectFile(CurrentPath, "TMX files (*.tmx)|*.tmx|All Files (*.*)|*.*");
        if (string.IsNullOrEmpty(file)) return;

        try
        {
            var entries = Core.Services.TmxService.ImportTmx(file);
            if (_translationMemory != null)
            {
                foreach (var e in entries)
                    _translationMemory.Add(e.SourceText, e.TargetText, e.SourceLang, e.TargetLang);
            }
            StatusText = $"Imported {entries.Count} TM entries from TMX.";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage($"TMX import failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ExportTmx()
    {
        if (_translationMemory == null || _translationMemory.Count == 0)
        {
            _messageService.ShowMessage("Translation memory is empty.");
            return;
        }

        var folder = _dialogService.SelectFolder(CurrentPath);
        if (string.IsNullOrEmpty(folder)) return;

        try
        {
            var filePath = Path.Combine(folder, "translation-memory.tmx");
            var entries = _translationMemory.GetAllEntries();
            Core.Services.TmxService.ExportTmx(entries, filePath);
            StatusText = $"Exported {entries.Count} TM entries to {filePath}";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage($"TMX export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearTm()
    {
        if (_translationMemory == null || _translationMemory.Count == 0) return;
        _translationMemory.Clear();
        GhostSuggestion = null;
        Suggestions.Clear();
        StatusText = "Translation memory cleared.";
    }

    /// <summary>Inject TM reference (called after construction when DI resolves the service).</summary>
    internal void SetTranslationMemory(ITranslationMemory tm) => _translationMemory = tm;

    #endregion

    #region Dictionary

    [RelayCommand]
    private void AddDictionaryTerm()
    {
        _messageService.ShowMessage("Dictionary feature coming soon.");
    }

    #endregion

    #region Single Key Translation

    [RelayCommand]
    private async Task TranslateSelectedKey()
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (string.IsNullOrEmpty(ns))
        {
            _messageService.ShowMessage("No key selected.");
            return;
        }

        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            _messageService.ShowMessage("No translations loaded.");
            return;
        }

        if (_bulkActionService == null)
        {
            _messageService.ShowMessage("Bulk action service is not available.");
            return;
        }

        var toTranslate = AllTranslation.Where(t => t.Namespace == ns).ToList();
        if (toTranslate.Count == 0)
        {
            _messageService.ShowMessage("No items found for the selected key.");
            return;
        }

        try
        {
            IsLoading = true;
            await _bulkActionService.PreTranslateAsync(toTranslate).ConfigureAwait(true);
            UpdateSummaryInfo();
            NotifyBulkValueChanges(toTranslate);
            StatusText = $"Translated key '{ns}' ({toTranslate.Count} language(s)).";
        }
        catch (Exception ex)
        {
            _messageService.ShowMessage($"Translation failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion
}

/// <summary>A single validation issue for display in the Issues panel.</summary>
public record ValidationIssueItem(
    string Key,
    string Message,
    Toucan.Core.Contracts.ValidationSeverity Severity = Toucan.Core.Contracts.ValidationSeverity.Warning,
    string? Namespace = null,
    string? Language = null,
    string? SuggestedFix = null);
