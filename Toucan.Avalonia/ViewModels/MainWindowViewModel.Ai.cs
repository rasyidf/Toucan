using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

/// <summary>AI Integration in the editor: the app-wide switch, Analyze, Clarity, and first-run onboarding.</summary>
public partial class MainWindowViewModel
{
    private const string AnalysisRuleId = "ai-analysis";
    private const string ClarityRuleId = "ai-clarity";

    /// <summary>The app-wide AI switch. When off, AI commands are hidden and the AI translation provider is not offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeTranslationsCommand), nameof(CheckSourceClarityCommand))]
    private bool isAiEnabled;

    public ObservableCollection<AnalysisResult> AnalysisResults { get; } = [];
    public ObservableCollection<ClarityResult> ClarityResults { get; } = [];

    private void InitAi()
    {
        IsAiEnabled = _ai.IsEnabled;
        // Settings → AI or onboarding changed the switch: follow it everywhere (menus, panel, provider list).
        _aiSettings.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshAiState);
    }

    private void RefreshAiState()
    {
        IsAiEnabled = _ai.IsEnabled;
        RefreshProviderChoices();
    }

    /// <summary>The context the AI features use: the project's, else the app default; asks for one when both are empty.</summary>
    private async Task<string?> RequireContextAsync(string title)
    {
        var context = ProjectSettings?.Context ?? AppOptions.Context;
        if (!string.IsNullOrWhiteSpace(context)) return context;

        context = await _dialogService.ShowPromptAsync(title,
            "Describe your application's domain (e.g. \"Banking app for savings accounts\"). AI uses it to choose the right terminology.");
        if (string.IsNullOrWhiteSpace(context)) return null;
        AppOptions.Context = context;
        _preferenceService.Save(AppOptions);
        return context;
    }

    /// <summary>Shows why AI cannot run (off, no key) and offers to open Settings → AI.</summary>
    private async Task<bool> EnsureAiReadyAsync(string featureId, string title)
    {
        var status = _ai.GetStatus();
        string? problem = status.Problem ?? (_ai.IsFeatureEnabled(featureId) ? null : "This AI feature is turned off.");
        if (problem == null) return true;

        if (await _messageService.ConfirmAsync($"{problem}\n\nOpen Settings → AI?", title))
            await OpenAiSettings();
        return false;
    }

    private void ReplaceIssues(string ruleId, IEnumerable<ValidationIssueItem> issues)
    {
        foreach (var old in ValidationIssues.Where(i => i.RuleId == ruleId).ToList()) ValidationIssues.Remove(old);
        foreach (var issue in issues) ValidationIssues.Add(issue);
        OnValidationIssuesChanged();
        SidePanelRegistry.Instance.Activate("issues");
    }

    private static ValidationSeverity ToValidation(AnalysisSeverity severity) => severity switch
    {
        AnalysisSeverity.Error => ValidationSeverity.Error,
        AnalysisSeverity.Warning => ValidationSeverity.Warning,
        _ => ValidationSeverity.Info,
    };

    // ───────────────────────── Analyze ─────────────────────────

    [RelayCommand(CanExecute = nameof(IsAiEnabled))]
    private async Task AnalyzeTranslations()
    {
        if (AllTranslation.Count == 0) return;
        if (!await EnsureAiReadyAsync(AiFeatureIds.Analyze, "Analyze")) return;
        var context = await RequireContextAsync("Application Context");
        if (context == null) return;

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
            var request = new AnalysisRequest { Items = items, ApplicationContext = context, SourceLanguage = primary, ProjectPath = HasProject ? CurrentPath : null };
            var progress = new Progress<PretranslationProgress>(p => StatusText = p.Message ?? $"Analyzing {p.Completed}/{p.Total}…");
            var results = (await Task.Run(() => _translationAnalyzer.AnalyzeAsync(request, progress))).Where(r => r.Confidence >= 0.6).ToList();

            AnalysisResults.Clear();
            foreach (var r in results) AnalysisResults.Add(r);
            ReplaceIssues(AnalysisRuleId, results.Select(r =>
                new ValidationIssueItem(r.Namespace, $"[{r.TargetLanguage}] {r.Issue}", ToValidation(r.Severity), r.Namespace, r.TargetLanguage, r.SuggestedFix, AnalysisRuleId)));
            StatusText = results.Count > 0 ? $"Analysis complete: {results.Count} issue(s)." : "Analysis complete: no issues found.";
        }
        catch (Exception ex) when (ex is AiUnavailableException or AiRequestException or HttpRequestException or TaskCanceledException)
        {
            StatusText = "Analysis failed.";
            await _messageService.ShowMessageAsync($"Analysis failed: {ex.Message}", "Analyze");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ───────────────────────── Clarity ─────────────────────────

    /// <summary>
    /// Reviews the source strings (primary language) for ambiguity and missing context. Findings go to the Issues panel;
    /// a suggested rewrite of the source can be applied from there like any other fix.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsAiEnabled))]
    private async Task CheckSourceClarity()
    {
        if (AllTranslation.Count == 0) return;
        if (!await EnsureAiReadyAsync(AiFeatureIds.Clarity, "Source Clarity")) return;

        var primary = PrimaryLanguage;
        var items = AllTranslation
            .Where(i => i.Language == primary && !string.IsNullOrWhiteSpace(i.Value))
            .GroupBy(i => i.Namespace).Select(g => new ClarityItem(g.Key, g.First().Value))
            .ToList();
        if (items.Count == 0)
        {
            await _messageService.ShowMessageAsync($"There are no source strings in {primary} to review.", "Source Clarity");
            return;
        }

        IsLoading = true;
        StatusText = "Reviewing source strings…";
        try
        {
            var request = new ClarityRequest
            {
                Items = items,
                SourceLanguage = primary,
                ApplicationContext = ProjectSettings?.Context ?? AppOptions.Context,
                ProjectPath = HasProject ? CurrentPath : null,
            };
            var progress = new Progress<PretranslationProgress>(p => StatusText = p.Message ?? $"Reviewing {p.Completed}/{p.Total}…");
            var results = (await Task.Run(() => _clarity.ReviewAsync(request, progress))).Where(r => r.Confidence >= 0.5).ToList();

            ClarityResults.Clear();
            foreach (var r in results) ClarityResults.Add(r);
            ReplaceIssues(ClarityRuleId, results.Select(r =>
            {
                var message = string.IsNullOrWhiteSpace(r.TranslatorNote) ? r.Issue : $"{r.Issue} Note for translators: {r.TranslatorNote}";
                return new ValidationIssueItem(r.Namespace, $"[{primary}] {message}", ToValidation(r.Severity), r.Namespace, primary, r.SuggestedSource, ClarityRuleId);
            }));
            StatusText = results.Count > 0 ? $"Clarity review complete: {results.Count} string(s) could be clearer." : "Clarity review complete: every source string is clear.";
        }
        catch (Exception ex) when (ex is AiUnavailableException or AiRequestException or HttpRequestException or TaskCanceledException)
        {
            StatusText = "Clarity review failed.";
            await _messageService.ShowMessageAsync($"Clarity review failed: {ex.Message}", "Source Clarity");
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ───────────────────────── Settings & onboarding ─────────────────────────

    [RelayCommand]
    private Task OpenAiSettings() => ShowPreferencesAtAsync(OptionsViewModel.AiPage);

    /// <summary>First run (or a new onboarding step since the last one the user saw): asks whether to use AI.</summary>
    public async Task RunOnboardingIfNeededAsync()
    {
        if (AppOptions.OnboardingVersion >= OnboardingViewModel.CurrentVersion) return;
        await _dialogService.ShowOnboardingAsync();
        AppOptions.OnboardingVersion = OnboardingViewModel.CurrentVersion;
        _preferenceService.Save(AppOptions);
        RefreshAiState();
    }
}
