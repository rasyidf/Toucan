using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Extensions;

namespace Toucan.Avalonia.ViewModels;

/// <summary>A suggestion row in the inspector (translation memory or similar key).</summary>
public sealed record SuggestionItem(string Label, string Text, string Detail);

/// <summary>A language row in the inspector's key details.</summary>
public sealed record KeyLanguageStatus(string Language, string Status, string Value);

/// <summary>Machine translation, validation, analysis, source code scanning, translation memory, and the inspector.</summary>
public partial class MainWindowViewModel
{
    // ───────────────────────── Pre-translation ─────────────────────────

    [RelayCommand]
    private async Task PreTranslateBulk()
    {
        if (AllTranslation.Count == 0)
        {
            await _messageService.ShowMessageAsync("Open a project first.", "Pre-translate");
            return;
        }
        FlushPendingEdits();
        await RunPreTranslateDialog(AllTranslation.ForParse().ToList());
    }

    private async Task RunPreTranslateDialog(List<TranslationItem> items)
    {
        var vm = new PreTranslateViewModel(OrderedLanguages(), items, _pretranslationService, _dialogService, _providerSettingsService, CurrentPath, PrimaryLanguage,
            UsableProviderNames(), ResolveProvider());
        var committed = await _dialogService.ShowPreTranslateAsync(vm);
        RememberProvider(vm.SelectedProvider);
        if (!committed || vm.AppliedCount == 0) return;

        NotifyBulkValueChanges(vm.AppliedItems);
        UpdateSummaryInfo();
        foreach (var t in PagingController.Data.SelectMany(g => g.AllItems)) t.Refresh();
        StatusText = $"Pre-translation applied to {vm.AppliedCount} value(s).";
    }

    /// <summary>Fills empty values of the given items with the last-used provider, without a preview.</summary>
    private async Task QuickTranslateAsync(List<TranslationItem> targets, string description)
    {
        var missing = targets.Where(t => string.IsNullOrEmpty(t.Value) && !string.IsNullOrWhiteSpace(t.Namespace)).ToList();
        if (missing.Count == 0)
        {
            StatusText = $"{description}: nothing to translate.";
            return;
        }

        var provider = ResolveProvider();
        IsLoading = true;
        StatusText = $"Translating {missing.Count} value(s) with {provider}…";
        try
        {
            var request = new PretranslationRequest
            {
                Provider = provider,
                Items = missing,
                ContextItems = AllTranslation,
                Options = new PretranslationOptions
                {
                    PreviewOnly = true,
                    ProviderOptions = ProviderOptionsBuilder.Build(provider, _providerSettingsService, CurrentPath)
                }
            };
            var result = await Task.Run(() => _pretranslationService.PreTranslateAsync(request));

            var applied = new List<TranslationItem>();
            foreach (var r in result.Items.Where(r => r.Succeeded && !string.IsNullOrEmpty(r.TranslatedValue)))
            {
                var item = missing.FirstOrDefault(i => i.Namespace == r.Namespace && i.Language == r.Language);
                if (item == null) continue;
                item.Value = r.TranslatedValue!;
                item.ChangeType = ChangeType.Suggestion;
                item.LastModifiedUtc = DateTime.UtcNow;
                applied.Add(item);
            }

            if (applied.Count > 0) NotifyBulkValueChanges(applied);
            foreach (var t in PagingController.Data.SelectMany(g => g.AllItems)) t.Refresh();
            UpdateSummaryInfo();

            var failed = result.Items.FirstOrDefault(r => !r.Succeeded);
            StatusText = failed != null && applied.Count == 0
                ? $"Translation failed: {failed.ErrorMessage}"
                : $"{description}: translated {applied.Count} of {missing.Count} value(s) with {provider}.";
            RecordTranslationRun(StatusText, result.Items);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException or NotSupportedException or TaskCanceledException)
        {
            await _messageService.ShowMessageAsync($"Translation failed: {ex.Message}\n\nCheck the provider settings (Settings → Translation Providers).", "Translate");
        }
        finally
        {
            IsLoading = false;
        }
    }

    internal Task TranslateKeyAsync(string ns) =>
        QuickTranslateAsync(AllTranslation.Where(t => t.Namespace == ns || (PluralService.IsPluralKey(t.Namespace) && PluralService.GetBaseKey(t.Namespace) == ns)).ToList(), $"Key {ns}");

    [RelayCommand]
    private Task TranslateSelectedKey()
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        return string.IsNullOrEmpty(ns) ? _messageService.ShowMessageAsync("Select a key first.", "Translate") : TranslateKeyAsync(ns);
    }

    [RelayCommand]
    private Task PreTranslateLanguage(SummaryItem? item) =>
        item == null ? Task.CompletedTask : QuickTranslateAsync(AllTranslation.Where(t => t.Language == item.Language).ToList(), item.Language);

    [RelayCommand]
    private Task PreTranslateVisible() =>
        QuickTranslateAsync(PagingController.Data.SelectMany(g => g.AllItems).Select(i => i.Model).ToList(), "Current view");

    [RelayCommand]
    private async Task ApproveAllForLanguage(SummaryItem? item)
    {
        if (item == null) return;
        var items = AllTranslation.Where(t => t.Language == item.Language && !string.IsNullOrEmpty(t.Value) && !t.IsApproved).ToList();
        if (items.Count == 0)
        {
            await _messageService.ShowMessageAsync("Nothing to approve.", "Approve All");
            return;
        }
        if (!await _messageService.ConfirmAsync($"Approve {items.Count} translated item(s) for '{item.Language}'?", "Approve All", "Approve", "Cancel")) return;
        ApproveItems(items);
    }

    [RelayCommand]
    private void ApproveVisible() =>
        ApproveItems(PagingController.Data.SelectMany(g => g.AllItems).Select(i => i.Model).Where(t => !string.IsNullOrEmpty(t.Value) && !t.IsApproved).ToList());

    private void ApproveItems(List<TranslationItem> items)
    {
        if (items.Count == 0) return;
        foreach (var t in items)
        {
            t.IsApproved = true;
            t.ApprovedAtUtc = DateTime.UtcNow;
        }
        NotifyBulkValueChanges(items);
        foreach (var t in PagingController.Data.SelectMany(g => g.AllItems)) t.Refresh();
        UpdateSummaryInfo();
        StatusText = $"Approved {items.Count} item(s).";
    }

    [RelayCommand]
    private Task GenerateStatisticsBulk() =>
        _dialogService.ShowStatisticsAsync(AllTranslation.Where(t => !IsNamespaceHidden(t.Namespace)));

    // ───────────────────────── Validation ─────────────────────────

    public ObservableCollection<ValidationIssueItem> ValidationIssues { get; } = [];
    public bool HasValidationIssues => ValidationIssues.Count > 0;
    public string ValidationSummary => ValidationIssues.Count == 0
        ? "No issues. Run validation to check placeholders, missing values, and more."
        : $"{ValidationIssues.Count(i => i.Severity == ValidationSeverity.Error)} error(s), {ValidationIssues.Count(i => i.Severity == ValidationSeverity.Warning)} warning(s), {ValidationIssues.Count(i => i.Severity == ValidationSeverity.Info)} info";

    [RelayCommand]
    private void RunValidation()
    {
        if (AllTranslation.Count == 0 || ProjectSettings == null) return;
        FlushPendingEdits();

        var settings = ProjectSettings;
        var originalPrimary = settings.PrimaryLanguage;
        settings.PrimaryLanguage = PrimaryLanguage;
        try
        {
            ShowValidationResults(_validationPipeline.RunAll(new ValidationContext { Items = AllTranslation, PrimaryLanguage = settings.PrimaryLanguage }).ToList());
        }
        finally
        {
            settings.PrimaryLanguage = originalPrimary;
        }
        SidePanelRegistry.Instance.Activate("issues");
        StatusText = ValidationIssues.Count == 0 ? "All validations passed." : ValidationSummary;
    }

    private void ShowValidationResults(IEnumerable<ValidationResult> results)
    {
        ValidationIssues.Clear();
        foreach (var r in results.OrderBy(r => r.Severity).ThenBy(r => r.Namespace, StringComparer.Ordinal))
        {
            ValidationIssues.Add(new ValidationIssueItem(r.Namespace ?? r.RuleId, string.IsNullOrEmpty(r.Language) ? r.Message : $"[{r.Language}] {r.Message}",
                r.Severity, r.Namespace, r.Language, r.SuggestedFix, r.RuleId));
        }
        OnValidationIssuesChanged();
    }

    private void OnValidationIssuesChanged()
    {
        OnPropertyChanged(nameof(HasValidationIssues));
        OnPropertyChanged(nameof(ValidationSummary));
        UpdateSummaryInfo();
    }

    [RelayCommand]
    private void DismissIssue(ValidationIssueItem? issue)
    {
        if (issue != null && ValidationIssues.Remove(issue)) OnValidationIssuesChanged();
    }

    [RelayCommand]
    private void DismissAllIssues()
    {
        ValidationIssues.Clear();
        OnValidationIssuesChanged();
    }

    [RelayCommand]
    private void ApplyFix(ValidationIssueItem? issue)
    {
        if (issue?.SuggestedFix == null || issue.Namespace == null) return;
        var target = AllTranslation.FirstOrDefault(t => t.Namespace == issue.Namespace && t.Language == issue.Language);
        if (target == null) return;

        _undoRedoService.Record(target.Namespace, target.Language, target.Value, issue.SuggestedFix);
        target.Value = issue.SuggestedFix;
        NotifyBulkValueChanges([target]);
        foreach (var t in PagingController.Data.SelectMany(g => g.AllItems).Where(t => ReferenceEquals(t.Model, target))) t.Refresh();
        ValidationIssues.Remove(issue);
        OnValidationIssuesChanged();
    }

    [RelayCommand]
    private void RevealIssue(ValidationIssueItem? issue)
    {
        if (issue?.Namespace != null) RevealKey(issue.Namespace);
    }

    // ───────────────────────── Plural & gender forms ─────────────────────────

    [RelayCommand]
    private void GeneratePluralForms() => GenerateForms("plural", PluralService.GetBaseKey, PluralService.GenerateMissingForms);

    [RelayCommand]
    private void GenerateGenderForms() => GenerateForms("gender", GenderService.GetBaseKey, GenderService.GenerateMissingForms);

    private void GenerateForms(string kind, Func<string, string> baseKeyOf, Func<string, string, IEnumerable<TranslationItem>, List<TranslationItem>> generate)
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (string.IsNullOrEmpty(ns)) return;

        var baseKey = baseKeyOf(ns);
        var added = new List<TranslationItem>();
        foreach (var lang in ProjectLanguages())
        {
            var missing = generate(baseKey, lang, AllTranslation);
            AllTranslation.AddRange(missing);
            added.AddRange(missing);
        }

        if (added.Count > 0)
        {
            MarkStructureChanged(added.Select(a => a.Namespace));
            RefreshTree();
            UpdateSummaryInfo();
            Search(baseKey, true);
        }
        StatusText = added.Count > 0 ? $"Generated {added.Count} {kind} forms for '{baseKey}'." : $"All {kind} forms already exist.";
    }

    // ───────────────────────── Source code ─────────────────────────

    [ObservableProperty] private bool sourceCodeScanned;
    [ObservableProperty] private string sourceCodeStatus = "Scan your source code to find where keys are used.";
    [ObservableProperty] private bool isScanning;

    public ObservableCollection<KeyUsage> SourceCodeUsages { get; } = [];
    public ObservableCollection<string> UndefinedKeys { get; } = [];

    [RelayCommand]
    private async Task ScanSourceCode()
    {
        if (!HasProject || IsScanning) return;
        var roots = ProjectSettings?.SourceRoots is { Count: > 0 } r ? r : [];
        var scanPath = roots.Count > 0 ? Path.GetFullPath(Path.Combine(CurrentPath, roots[0])) : CurrentPath;

        IsScanning = true;
        StatusText = "Scanning source code…";
        try
        {
            var result = await Task.Run(() => _sourceCodeService.ScanAsync(scanPath));
            SourceCodeScanned = true;
            SourceCodeStatus = string.Create(CultureInfo.CurrentCulture,
                $"{result.KeysFound} keys in {result.FilesScanned} files ({result.Duration.TotalSeconds:F1}s) · {Path.GetFileName(scanPath)}");
            StatusText = SourceCodeStatus;

            SourceCodeUsages.Clear();
            foreach (var key in _sourceCodeService.GetAllKeys().OrderBy(k => k, StringComparer.Ordinal))
                foreach (var usage in _sourceCodeService.FindUsages(key))
                    SourceCodeUsages.Add(usage);

            UndefinedKeys.Clear();
            foreach (var k in _sourceCodeService.GetUndefinedKeys(AllTranslation.ToNamespaces()).OrderBy(k => k, StringComparer.Ordinal))
                UndefinedKeys.Add(k);
            RefreshInspector();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SourceCodeStatus = "Scan failed: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task SelectSourceRoot()
    {
        if (!HasProject || ProjectSettings == null) return;
        var selected = await _dialogService.SelectFolderAsync(CurrentPath, "Source Code Folder");
        if (string.IsNullOrEmpty(selected)) return;
        ProjectSettings.SourceRoots = [Path.GetRelativePath(CurrentPath, selected)];
        ProjectSettings.Save();
        await ScanSourceCode();
    }

    [RelayCommand]
    private void FilterUsedKeys()
    {
        if (!_sourceCodeService.HasScanData) return;
        FilteredBySourceUsage = "used";
        Search(SearchText, true);
    }

    [RelayCommand]
    private void FilterUnusedKeys()
    {
        if (!_sourceCodeService.HasScanData) return;
        FilteredBySourceUsage = "unused";
        Search(SearchText, true);
    }

    [RelayCommand]
    private void ClearSourceFilter()
    {
        FilteredBySourceUsage = null;
        Search(SearchText, true);
    }

    [RelayCommand]
    private async Task AddUndefinedKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        await CreateNewItemAsync(key);
        UndefinedKeys.Remove(key);
    }

    /// <summary>Opens the file of a usage in the configured external editor.</summary>
    [RelayCommand]
    private void OpenUsage(KeyUsage? usage)
    {
        if (usage == null) return;
        var roots = ProjectSettings?.SourceRoots is { Count: > 0 } r ? r : [];
        var root = roots.Count > 0 ? Path.GetFullPath(Path.Combine(CurrentPath, roots[0])) : CurrentPath;
        var fullPath = Path.IsPathRooted(usage.FilePath) ? usage.FilePath : Path.Combine(root, usage.FilePath);
        var editor = ProjectSettings?.ExternalEditor ?? "code --goto \"{file}:{line}\"";
        PlatformService.RunShellCommand(editor
            .Replace("{file}", fullPath, StringComparison.Ordinal)
            .Replace("{line}", usage.Line.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [RelayCommand]
    private void OpenInEditor(string? key)
    {
        if (!string.IsNullOrEmpty(key)) OpenUsage(_sourceCodeService.FindUsages(key).FirstOrDefault());
    }

    // ───────────────────────── AI analysis ─────────────────────────

    public ObservableCollection<AnalysisResult> AnalysisResults { get; } = [];

    [RelayCommand]
    private async Task AnalyzeTranslations()
    {
        if (AllTranslation.Count == 0) return;

        var context = ProjectSettings?.Context ?? AppOptions.Context;
        if (string.IsNullOrWhiteSpace(context))
        {
            context = await _dialogService.ShowPromptAsync("Application Context",
                "Describe your application's domain (e.g. \"Banking app for savings accounts\"). The analyzer uses it to check terminology.");
            if (string.IsNullOrWhiteSpace(context)) return;
            AppOptions.Context = context;
            _preferenceService.Save(AppOptions);
        }

        var primary = PrimaryLanguage;
        var sourceMap = AllTranslation.Where(i => i.Language == primary && !string.IsNullOrEmpty(i.Value))
            .GroupBy(i => i.Namespace).ToDictionary(g => g.Key, g => g.First().Value);
        var items = AllTranslation
            .Where(i => i.Language != primary && !string.IsNullOrEmpty(i.Value) && sourceMap.ContainsKey(i.Namespace))
            .Select(i => new AnalysisItem(i.Namespace, sourceMap[i.Namespace], i.Value, i.Language))
            .ToList();
        if (items.Count == 0)
        {
            await _messageService.ShowMessageAsync("There are no translated values to analyze.", "Analyze");
            return;
        }

        IsLoading = true;
        StatusText = "Analyzing translations…";
        try
        {
            var request = new AnalysisRequest
            {
                Items = items,
                ApplicationContext = context,
                SourceLanguage = primary,
                ProviderOptions = ProviderOptionsBuilder.Build("OpenAI", _providerSettingsService, CurrentPath)
            };
            var progress = new Progress<PretranslationProgress>(p => StatusText = p.Message ?? $"Analyzing {p.Completed}/{p.Total}…");
            var results = await Task.Run(() => _translationAnalyzer.AnalyzeAsync(request, progress));

            AnalysisResults.Clear();
            ValidationIssues.Clear();
            foreach (var r in results.Where(r => r.Confidence >= 0.6))
            {
                AnalysisResults.Add(r);
                var severity = r.Severity switch
                {
                    AnalysisSeverity.Error => ValidationSeverity.Error,
                    AnalysisSeverity.Warning => ValidationSeverity.Warning,
                    _ => ValidationSeverity.Info
                };
                ValidationIssues.Add(new ValidationIssueItem(r.Namespace, $"[{r.TargetLanguage}] {r.Issue}", severity, r.Namespace, r.TargetLanguage, r.SuggestedFix, "ai-analysis"));
            }
            OnValidationIssuesChanged();
            SidePanelRegistry.Instance.Activate("issues");
            StatusText = AnalysisResults.Count > 0 ? $"Analysis complete: {AnalysisResults.Count} issue(s)." : "Analysis complete: no issues found.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException or TaskCanceledException)
        {
            await _messageService.ShowMessageAsync($"Analysis failed: {ex.Message}\n\nThe analyzer uses the OpenAI provider settings.", "Analyze");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ───────────────────────── Inspector ─────────────────────────

    [ObservableProperty] private string selectedKeyNamespace = string.Empty;
    [ObservableProperty] private string selectedKeyComment = string.Empty;
    [ObservableProperty] private string selectedKeyAuditInfo = string.Empty;
    [ObservableProperty] private bool hasSelectedKeyDetails;
    [ObservableProperty] private string? ghostSuggestion;
    private TranslationItemViewModel? _ghostTarget;

    partial void OnGhostSuggestionChanged(string? value)
    {
        if (_ghostTarget != null) _ghostTarget.GhostText = null;
        _ghostTarget = string.IsNullOrEmpty(value) ? null : FocusedTranslationItem;
        if (_ghostTarget != null) _ghostTarget.GhostText = value;
    }

    /// <summary>Accepts the ghost text into an empty field. Returns false when there was nothing to accept.</summary>
    internal bool AcceptGhostText(TranslationItemViewModel item)
    {
        if (IsAuditMode || !item.IsEmpty || string.IsNullOrEmpty(item.GhostText)) return false;
        item.Value = item.GhostText;
        item.GhostText = null;
        return true;
    }

    public ObservableCollection<KeyLanguageStatus> SelectedKeyLanguages { get; } = [];
    public ObservableCollection<SuggestionItem> Suggestions { get; } = [];
    public ObservableCollection<SuggestionItem> MemoryMatches { get; } = [];
    public ObservableCollection<KeyUsage> SelectedKeyUsages { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;
    public bool HasMemoryMatches => MemoryMatches.Count > 0;
    public bool HasSelectedKeyUsages => SelectedKeyUsages.Count > 0;

    /// <summary>Refreshes the inspector for the currently selected key.</summary>
    private void RefreshInspector()
    {
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        SelectedKeyLanguages.Clear();
        Suggestions.Clear();
        SelectedKeyUsages.Clear();

        var items = string.IsNullOrEmpty(ns) ? [] : AllTranslation.Where(t => t.Namespace == ns).ToList();
        HasSelectedKeyDetails = items.Count > 0;
        SelectedKeyNamespace = ns ?? string.Empty;

        if (items.Count > 0)
        {
            SelectedKeyComment = items.FirstOrDefault(t => !string.IsNullOrEmpty(t.Comment))?.Comment ?? string.Empty;
            foreach (var item in items.OrderBy(t => t.Language == PrimaryLanguage ? 0 : 1).ThenBy(t => t.Language, StringComparer.Ordinal))
            {
                var status = string.IsNullOrEmpty(item.Value) ? "empty" : item.IsApproved ? "approved" : item.ChangeType == ChangeType.Suggestion ? "machine" : "translated";
                SelectedKeyLanguages.Add(new KeyLanguageStatus(item.Language, status, item.Value));
            }

            var audited = items.Where(t => t.LastModifiedUtc.HasValue).OrderByDescending(t => t.LastModifiedUtc).FirstOrDefault();
            SelectedKeyAuditInfo = audited == null
                ? "No change history recorded."
                : $"Last modified {audited.LastModifiedUtc!.Value.ToLocalTime():g} ({audited.ChangeType})"
                  + (audited.ApprovedAtUtc.HasValue ? $"\nApproved {audited.ApprovedAtUtc.Value.ToLocalTime():g}" : string.Empty);

            // Keys whose primary value resembles this one — often reusable wording.
            var source = items.FirstOrDefault(t => t.Language == PrimaryLanguage && !string.IsNullOrWhiteSpace(t.Value));
            if (source != null)
            {
                var similar = AllTranslation
                    .Where(t => t.Language == PrimaryLanguage && t.Namespace != ns && !string.IsNullOrWhiteSpace(t.Value))
                    .Select(t => (Item: t, Score: _fuzzySearch.ComputeTrigramSimilarity(source.Value, t.Value)))
                    .Where(x => x.Score > 0.35)
                    .OrderByDescending(x => x.Score)
                    .Take(6);
                foreach (var (item, score) in similar)
                    Suggestions.Add(new SuggestionItem(item.Namespace, item.Value, $"{score:P0} similar"));
            }

            if (_sourceCodeService.HasScanData)
                foreach (var u in _sourceCodeService.FindUsages(ns!).Take(20)) SelectedKeyUsages.Add(u);
        }
        else
        {
            SelectedKeyComment = string.Empty;
            SelectedKeyAuditInfo = string.Empty;
        }

        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(HasSelectedKeyUsages));
        RefreshMemoryMatches();
    }

    /// <summary>Looks up TM matches for the selected key's source text in each target language.</summary>
    private void RefreshMemoryMatches()
    {
        MemoryMatches.Clear();
        var ns = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        var source = string.IsNullOrEmpty(ns) ? null : AllTranslation.FirstOrDefault(t => t.Namespace == ns && t.Language == PrimaryLanguage && !string.IsNullOrWhiteSpace(t.Value));
        if (source != null)
        {
            var threshold = AppOptions.TmSimilarityThreshold;
            var max = Math.Max(1, AppOptions.TmMaxSuggestions);
            foreach (var lang in OrderedLanguages().Where(l => l != PrimaryLanguage))
            {
                foreach (var m in _translationMemory.Search(source.Value, PrimaryLanguage, lang, threshold, max))
                    MemoryMatches.Add(new SuggestionItem(lang, m.TargetText, $"{m.Similarity:P0} · {m.SourceText}"));
            }
        }
        OnPropertyChanged(nameof(HasMemoryMatches));
        OnPropertyChanged(nameof(TmEntryCount));
    }

    internal void UpdateGhostSuggestion(TranslationItemViewModel item)
    {
        GhostSuggestion = null;
        if (_ghostTarget != null) { _ghostTarget.GhostText = null; _ghostTarget = null; }
        if (!AppOptions.TmAutoSuggest || item.Language == PrimaryLanguage) return;
        var source = AllTranslation.FirstOrDefault(t => t.Namespace == item.Namespace && t.Language == PrimaryLanguage);
        if (source == null || string.IsNullOrWhiteSpace(source.Value)) return;
        GhostSuggestion = _translationMemory.Search(source.Value, PrimaryLanguage, item.Language, AppOptions.TmSimilarityThreshold, 1).FirstOrDefault()?.TargetText;
    }

    // ───────────────────────── Translation memory ─────────────────────────

    public string TmEntryCount => _translationMemory.Count.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Learns (source → target) pairs from keys edited this session.</summary>
    private void FeedTranslationMemory(IEnumerable<string> keys)
    {
        var keySet = keys.ToHashSet(StringComparer.Ordinal);
        if (keySet.Count == 0) return;
        var primary = PrimaryLanguage;
        var sources = AllTranslation.Where(t => t.Language == primary && keySet.Contains(t.Namespace) && !string.IsNullOrWhiteSpace(t.Value))
            .GroupBy(t => t.Namespace).ToDictionary(g => g.Key, g => g.First().Value);
        var entries = AllTranslation
            .Where(t => t.Language != primary && keySet.Contains(t.Namespace) && !string.IsNullOrWhiteSpace(t.Value) && sources.ContainsKey(t.Namespace))
            .Select(t => new TranslationMemoryEntry(sources[t.Namespace], t.Value, primary, t.Language, DateTime.UtcNow))
            .ToList();
        if (entries.Count > 0) _translationMemory.AddRange(entries);
        OnPropertyChanged(nameof(TmEntryCount));
    }

    [RelayCommand]
    private void LearnProjectIntoMemory()
    {
        FeedTranslationMemory(AllTranslation.ToNamespaces());
        StatusText = $"Translation memory now has {TmEntryCount} entries.";
        RefreshMemoryMatches();
    }

    [RelayCommand]
    private async Task ImportTmx()
    {
        var file = await _dialogService.SelectFileAsync(CurrentPath, "Import TMX", [new FileFilter("TMX", "*.tmx"), new FileFilter("All files", "*")]);
        if (file == null) return;
        try
        {
            var entries = TmxService.ImportTmx(file);
            _translationMemory.AddRange(entries.Select(e => new TranslationMemoryEntry(e.SourceText, e.TargetText, e.SourceLang, e.TargetLang, e.CreatedDate ?? DateTime.UtcNow)));
            StatusText = $"Imported {entries.Count} TM entries.";
            RefreshMemoryMatches();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException)
        {
            await _messageService.ShowMessageAsync($"TMX import failed: {ex.Message}", "Translation Memory");
        }
    }

    [RelayCommand]
    private async Task ExportTmx()
    {
        if (_translationMemory.Count == 0)
        {
            await _messageService.ShowMessageAsync("Translation memory is empty.", "Translation Memory");
            return;
        }
        var file = await _dialogService.SaveFileAsync(CurrentPath, "translation-memory.tmx", "Export TMX", [new FileFilter("TMX", "*.tmx")]);
        if (file == null) return;
        try
        {
            var entries = _translationMemory.GetAllEntries();
            TmxService.ExportTmx(entries, file);
            StatusText = $"Exported {entries.Count} TM entries.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _messageService.ShowMessageAsync($"TMX export failed: {ex.Message}", "Translation Memory");
        }
    }

    [RelayCommand]
    private async Task ClearTm()
    {
        if (_translationMemory.Count == 0) return;
        if (!await _messageService.ConfirmAsync($"Delete all {TmEntryCount} translation memory entries?", "Translation Memory", "Delete", "Cancel")) return;
        _translationMemory.Clear();
        GhostSuggestion = null;
        RefreshMemoryMatches();
        StatusText = "Translation memory cleared.";
    }
}
