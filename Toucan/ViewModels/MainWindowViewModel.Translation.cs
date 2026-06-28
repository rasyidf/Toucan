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
using Toucan.Services;
using Toucan.Views.Dialogs;

namespace Toucan.ViewModels;

/// <summary>
/// Translation operations: pre-translate, provider options, validation, analysis, bulk actions, translation memory/suggestions.
/// </summary>
internal partial class MainWindowViewModel
{
    #region Pre-Translation & Bulk Actions

    [RelayCommand]
    private async Task PreTranslateBulk()
    {
        try
        {
            if (AllTranslation == null || AllTranslation.Count == 0)
            {
                _messageService.ShowMessage("No translations loaded to pre-translate.");
                return;
            }

            // If we have a UI-capable pretranslation engine, show the Pre-Translate dialog instead
            if (_pretranslationService != null)
            {
                // gather language list
                List<string> languages = AllTranslation.ToLanguages().ToList();

                var vm = _preTranslateFactory != null ? _preTranslateFactory(languages, AllTranslation, _pretranslationService) : new PreTranslateViewModel(languages, AllTranslation, _pretranslationService);
                bool result = _dialogService.ShowPreTranslate(vm);

                if (result)
                {
                    UpdateSummaryInfo();
                    IsDirty = true;
                    StatusText = "Pre-translation completed.";
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
            IsDirty = true;
            StatusText = "Pre-translation completed.";
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

        StatisticsDialog dialog = new(AllTranslation, System.Windows.Application.Current?.MainWindow);
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
            IsDirty = true;
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
            IsDirty = true;
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
            IsDirty = true;
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
        IsDirty = true;
        StatusText = $"Approved {languageItems.Count} item(s) for {item.Language}.";
    }

    #endregion

    #region Source Code & Analysis

    // --- Source Code Integration ---

    [ObservableProperty]
    private bool sourceCodeScanned;

    [ObservableProperty]
    private string sourceCodeStatus = string.Empty;

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
    }

    [RelayCommand]
    private void FilterUsedKeys()
    {
        if (_sourceCodeService == null || !_sourceCodeService.HasScanData || AllTranslation == null)
        {
            return;
        }

        var allKeys = AllTranslation.Select(t => t.Namespace).Distinct();
        HashSet<string> unused = _sourceCodeService.GetUnusedKeys(allKeys).ToHashSet();
        // Filter to show only USED keys
        Search("", true); // reset first
        FilteredBySourceUsage = "used";
        OnPropertyChanged(nameof(FilteredBySourceUsage));
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
    }

    [RelayCommand]
    private void ClearSourceFilter()
    {
        FilteredBySourceUsage = null;
        Search("", true);
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
        if (AllTranslation == null || SelectedNode == null)
        {
            return;
        }

        // ponytail: naive fuzzy match — find translations with similar namespace or value
        var current = AllTranslation.FirstOrDefault(t => t.Namespace == SelectedNode.Namespace);
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

    #region Validation (Placeholders, Plural, Gender)

    [RelayCommand]
    private void ValidatePlaceholders()
    {
        if (AllTranslation == null || AllTranslation.Count == 0)
        {
            return;
        }

        List<string> languages = AllTranslation.ToLanguages().ToList();
        var primary = languages.FirstOrDefault() ?? "en";
        List<string> issues = new();

        List<TranslationItem> sourceItems = AllTranslation.Where(t => t.Language == primary && !string.IsNullOrEmpty(t.Value)).ToList();
        foreach (var source in sourceItems)
        {
            foreach (var lang in languages.Where(l => l != primary))
            {
                var target = AllTranslation.FirstOrDefault(t => t.Namespace == source.Namespace && t.Language == lang);
                if (target == null || string.IsNullOrEmpty(target.Value))
                {
                    continue;
                }

                var result = PlaceholderService.Validate(source.Value, target.Value);
                if (!result.IsValid)
                {
                    issues.Add($"{source.Namespace} [{lang}]: missing={string.Join(",", result.Missing)} extra={string.Join(",", result.Extra)}");
                }
            }
        }

        if (issues.Count == 0)
        {
            _messageService.ShowMessage("All placeholders are consistent across translations.");
        }
        else
        {
            _messageService.ShowMessage($"{issues.Count} placeholder issue(s):\n" + string.Join("\n", issues.Take(20)));
        }
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
        var added = 0;
        foreach (var lang in languages)
        {
            var missing = PluralService.GenerateMissingForms(baseKey, lang, AllTranslation);
            AllTranslation.AddRange(missing);
            added += missing.Count;
        }
        if (added > 0) { RefreshTree(); UpdateSummaryInfo(); IsDirty = true; }
        StatusText = added > 0 ? $"Generated {added} plural forms for '{baseKey}'" : "All plural forms already exist.";
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
        var added = 0;
        foreach (var lang in languages)
        {
            var missing = GenderService.GenerateMissingForms(baseKey, lang, AllTranslation);
            AllTranslation.AddRange(missing);
            added += missing.Count;
        }
        if (added > 0) { RefreshTree(); UpdateSummaryInfo(); IsDirty = true; }
        StatusText = added > 0 ? $"Generated {added} gender forms for '{baseKey}'" : "All gender forms already exist.";
    }

    #endregion

    #region Machine Translation Visibility

    [ObservableProperty]
    private bool showMachineTranslations;

    [RelayCommand]
    private void ToggleShowMachineTranslations()
    {
        ShowMachineTranslations = !ShowMachineTranslations;
        // ponytail: placeholder toggle — visual indicator only for now
        // upgrade path: filter/highlight machine-translated values when metadata is tracked
        StatusText = ShowMachineTranslations ? "Showing machine translations" : "Machine translations hidden";
    }

    #endregion
}
