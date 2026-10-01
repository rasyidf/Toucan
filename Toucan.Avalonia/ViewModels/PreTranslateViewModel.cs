using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Avalonia.ViewModels;

public partial class LanguageItem(string name, bool isSelected = false) : ObservableObject
{
    [ObservableProperty] private string name = name;
    [ObservableProperty] private bool isSelected = isSelected;
}

/// <summary>Builds provider option dictionaries (API keys, endpoints, context, formality).</summary>
internal static class ProviderOptionsBuilder
{
    public static Dictionary<string, string> Build(string provider, IProviderSettingsService? settingsService, string? projectPath)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (settingsService != null)
        {
            // Project-scoped settings override app-wide ones, key by key.
            var sources = new List<IEnumerable<ProviderSettings>> { settingsService.LoadAppProviderSettings() };
            if (!string.IsNullOrEmpty(projectPath)) sources.Add(settingsService.LoadProjectProviderSettings(projectPath));

            foreach (var source in sources)
            {
                var match = source.FirstOrDefault(p => string.Equals(p.Provider, provider, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;
                foreach (var kv in match.Options.Where(kv => !string.IsNullOrEmpty(kv.Value))) dict[kv.Key] = kv.Value;
                foreach (var kv in match.Secrets.Where(kv => !string.IsNullOrEmpty(kv.Value))) dict[kv.Key] = kv.Value;
            }
        }

        var project = string.IsNullOrEmpty(projectPath) ? null : ProjectSettings.LoadFrom(projectPath);
        var opts = AppOptions.LoadFromDisk();
        var context = project?.Context ?? opts.Context;
        var formality = project?.Formality ?? opts.Formality;
        if (!string.IsNullOrWhiteSpace(context)) dict["context"] = context;
        if (!string.IsNullOrWhiteSpace(formality) && formality != "Default") dict["formality"] = formality.ToLowerInvariant();
        return dict;
    }
}

/// <summary>
/// Pre-translate dialog: runs the selected provider as a dry run, shows the results as a preview,
/// and only writes values when the user commits.
/// </summary>
public partial class PreTranslateViewModel : ObservableObject
{
    private readonly IPretranslationService? _pretranslationService;
    private readonly IReadOnlyList<TranslationItem> _sourceItems;
    private readonly IDialogService? _dialogService;
    private readonly IProviderSettingsService? _providerSettingsService;
    private readonly string? _projectPath;
    private CancellationTokenSource? _cts;

    public PreTranslateViewModel(
        IEnumerable<string> languages,
        IEnumerable<TranslationItem> sourceItems,
        IPretranslationService? pretranslation = null,
        IDialogService? dialogService = null,
        IProviderSettingsService? providerSettingsService = null,
        string? projectPath = null,
        string? primaryLanguage = null)
    {
        _pretranslationService = pretranslation;
        _sourceItems = sourceItems.ToList();
        _dialogService = dialogService;
        _providerSettingsService = providerSettingsService;
        _projectPath = projectPath;

        foreach (var l in languages)
        {
            // The source language is rarely a translation target; leave it unchecked by default.
            AvailableLanguages.Add(new LanguageItem(l, !string.Equals(l, primaryLanguage, StringComparison.OrdinalIgnoreCase)));
        }

        var last = AppOptions.LoadFromDisk().LastProvider;
        selectedProvider = Providers.Contains(last) ? last : Providers[0];
    }

    public static IReadOnlyList<string> Providers { get; } = ["Google", "DeepL", "Microsoft", "OpenAI", "Custom", "Mock"];

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } = [];
    public ObservableCollection<PretranslationItemResult> PreviewResults { get; } = [];

    [ObservableProperty] private string selectedProvider;
    [ObservableProperty] private bool overwriteExisting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int progressCompleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int progressTotal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(CommitCommand))]
    private bool isRunning;

    [ObservableProperty] private string statusMessage = "Choose a provider and target languages, then run a preview.";

    public double ProgressPercent => ProgressTotal == 0 ? 0 : Math.Clamp((double)ProgressCompleted / ProgressTotal * 100.0, 0, 100);

    public bool HasPreview => PreviewResults.Count > 0;

    /// <summary>Number of values written by the last commit.</summary>
    public int AppliedCount { get; private set; }

    /// <summary>Items whose values were written by the last commit.</summary>
    public List<TranslationItem> AppliedItems { get; } = [];

    public Action<bool>? CloseAction { get; set; }

    partial void OnSelectedProviderChanged(string value)
    {
        var opts = AppOptions.LoadFromDisk();
        opts.LastProvider = value;
        opts.ToDisk();
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        if (_pretranslationService == null) return;

        var selected = AvailableLanguages.Where(l => l.IsSelected).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = _sourceItems.Where(i => selected.Contains(i.Language) && !string.IsNullOrWhiteSpace(i.Namespace)).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Select at least one target language.";
            return;
        }

        PreviewResults.Clear();
        OnPropertyChanged(nameof(HasPreview));

        var request = new PretranslationRequest
        {
            Provider = SelectedProvider,
            Items = targets,
            ContextItems = _sourceItems,
            Options = new PretranslationOptions
            {
                Overwrite = OverwriteExisting,
                PreviewOnly = true,
                ProviderOptions = ProviderOptionsBuilder.Build(SelectedProvider, _providerSettingsService, _projectPath)
            }
        };

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressCompleted = 0;
        ProgressTotal = 0;
        StatusMessage = $"Translating with {SelectedProvider}…";

        var progress = new Progress<PretranslationProgress>(p =>
        {
            ProgressCompleted = p.Completed;
            ProgressTotal = p.Total;
        });

        try
        {
            var result = await Task.Run(() => _pretranslationService.PreTranslateAsync(request, progress, _cts.Token));
            foreach (var r in result.Items) PreviewResults.Add(r);
            var failed = result.Items.Count(i => !i.Succeeded);
            StatusMessage = result.Items.Count == 0
                ? "Nothing to translate: every selected value is filled (enable Overwrite to replace them) or has no source text."
                : $"{result.Items.Count - failed} translated, {failed} failed. Review, then Apply.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            StatusMessage = "Translation failed: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsRunning)
        {
            _cts?.Cancel();
            return;
        }
        CloseAction?.Invoke(false);
    }

    [RelayCommand]
    private Task OpenProviderSettings() => _dialogService?.ShowProviderSettingsAsync(_projectPath) ?? Task.CompletedTask;

    [RelayCommand]
    private void SelectAllLanguages()
    {
        var all = AvailableLanguages.All(l => l.IsSelected);
        foreach (var l in AvailableLanguages) l.IsSelected = !all;
    }

    private bool CanCommit() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private void Commit()
    {
        AppliedItems.Clear();
        foreach (var pr in PreviewResults.Where(p => p.Succeeded && !string.IsNullOrEmpty(p.TranslatedValue)))
        {
            var item = _sourceItems.FirstOrDefault(i => i.Namespace == pr.Namespace && i.Language == pr.Language);
            if (item == null || (!OverwriteExisting && !string.IsNullOrEmpty(item.Value))) continue;

            item.Value = pr.TranslatedValue!;
            item.ChangeType = ChangeType.Suggestion;
            item.LastModifiedUtc = DateTime.UtcNow;
            item.IsApproved = false;
            AppliedItems.Add(item);
        }
        AppliedCount = AppliedItems.Count;
        CloseAction?.Invoke(true);
    }
}
