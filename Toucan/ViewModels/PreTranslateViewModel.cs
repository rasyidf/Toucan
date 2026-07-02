using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Services;

namespace Toucan.ViewModels;

public partial class LanguageItem : ObservableObject
{
    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private bool isSelected;

    public LanguageItem(string name, bool isSelected = false)
    {
        Name = name;
        IsSelected = isSelected;
    }
}

public partial class PreTranslateViewModel : ObservableObject
{
    public static Func<string, object, bool> StringEqualsConverter = (param, value) => string.Equals((string?)param, (string?)value, StringComparison.InvariantCultureIgnoreCase);

    [ObservableProperty]
    private string selectedProvider = Toucan.Core.Options.AppOptions.LoadFromDisk().LastProvider ?? "Google";

    partial void OnSelectedProviderChanged(string value)
    {
        // Remember last translation service
        var opts = Toucan.Core.Options.AppOptions.LoadFromDisk();
        opts.LastProvider = value;
        opts.ToDisk();
    }

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } = [];

    [ObservableProperty]
    private bool resetApproved = true;

    [ObservableProperty]
    private bool translateSelectedOnly = true;

    [ObservableProperty]
    private bool overwriteExisting = false;

    private readonly IPretranslationService? _pretranslationService;
    private readonly IEnumerable<TranslationItem>? _sourceItems;
    private readonly IDialogService? _dialogService;
    private readonly IProviderSettingsService? _providerSettingsService;

    private System.Threading.CancellationTokenSource? _cts;

    public ObservableCollection<PretranslationItemResult> PreviewResults { get; } = [];

    [ObservableProperty]
    private int progressCompleted;

    [ObservableProperty]
    private int progressTotal;

    // ProgressBar.Value expects a double. Return double here to avoid conversion issues
    // and ensure the value is clamped to the 0..100 range.
    public double ProgressPercent => ProgressTotal == 0 ? 0.0 : Math.Clamp((double)ProgressCompleted / ProgressTotal * 100.0, 0.0, 100.0);

    [ObservableProperty]
    private bool isRunning;

    public PreTranslateViewModel()
    {
        // default languages for preview
        AvailableLanguages.Add(new LanguageItem("id-ID", true));
        AvailableLanguages.Add(new LanguageItem("zh-CN", true));
        AvailableLanguages.Add(new LanguageItem("fr-FR", false));
    }

    public PreTranslateViewModel(IEnumerable<string> languages, IEnumerable<TranslationItem>? sourceItems = null, IPretranslationService? pretranslation = null, IDialogService? dialogService = null, IProviderSettingsService? providerSettingsService = null)
    {
        _pretranslationService = pretranslation;
        _sourceItems = sourceItems;
        _dialogService = dialogService;
        _providerSettingsService = providerSettingsService;

        foreach (string l in languages)
        {
            AvailableLanguages.Add(new LanguageItem(l, true));
        }
    }

    // Cancel command for running preview will be provided below (cancellation-token aware).

    [RelayCommand]
    private async Task Start()
    {
        if (IsRunning)
        {
            return;
        }

        // Gather request and call pretranslation service if available
        if (_pretranslationService == null || _sourceItems == null)
        {
            return;
        }

        var selectedLanguages = AvailableLanguages.Where(l => l.IsSelected).Select(l => l.Name).ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        IEnumerable<TranslationItem> itemsToProcess = _sourceItems;
        if (TranslateSelectedOnly)
        {
            itemsToProcess = itemsToProcess.Where(i => selectedLanguages.Contains(i.Language));
        }

        PreviewResults.Clear();

        var req = new PretranslationRequest
        {
            Provider = SelectedProvider,
            Items = itemsToProcess.ToList(),
            ContextItems = _sourceItems,
            Options = new PretranslationOptions
            {
                Overwrite = OverwriteExisting,
                PreviewOnly = true,
                ProviderOptions = BuildProviderOptions()
            }
        };

        if (_pretranslationService == null)
        {
            return;
        }

        _cts?.Dispose();
        _cts = new System.Threading.CancellationTokenSource();
        IsRunning = true;
        ProgressCompleted = 0;
        ProgressTotal = 0;

        var progress = new Progress<PretranslationProgress>(p =>
        {
            ProgressCompleted = p.Completed;
            ProgressTotal = p.Total;
            OnPropertyChanged(nameof(ProgressPercent));
        });

        var result = await _pretranslationService.PreTranslateAsync(req, progress, _cts.Token).ConfigureAwait(true);

        // populate preview results on the UI thread
        PreviewResults.Clear();
        foreach (var r in result.Items)
        {
            PreviewResults.Add(r);
        }

        IsRunning = false;
    }

    [RelayCommand]
    private void OpenProviderSettings()
    {
        _ = _dialogService?.ShowProviderSettings();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    [RelayCommand]
    private Task Commit()
    {
        // Apply preview results to source items directly (so we don't re-run the provider)
        if (PreviewResults == null || !_sourceItems?.Any() == true)
        {
            return Task.CompletedTask;
        }

        var items = _sourceItems!.ToList();

        foreach (var pr in PreviewResults)
        {
            if (string.IsNullOrEmpty(pr.TranslatedValue))
            {
                continue;
            }

            var item = items.FirstOrDefault(i => (i.Namespace ?? string.Empty) == pr.Namespace && i.Language == pr.Language);
            if (item == null)
            {
                continue;
            }

            // honor overwrite
            if (OverwriteExisting || string.IsNullOrEmpty(item.Value))
            {
                item.Value = pr.TranslatedValue;
            }
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, string> BuildProviderOptions()
    {
        var dict = new Dictionary<string, string>();

        // Load saved provider config (api keys, endpoints, etc.)
        if (_providerSettingsService != null)
        {
            var all = _providerSettingsService.LoadAppProviderSettings();
            var match = all.FirstOrDefault(p => string.Equals(p.Provider, SelectedProvider, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                foreach (var kv in match.Options)
                {
                    dict[kv.Key] = kv.Value;
                }

                foreach (var kv in match.Secrets)
                {
                    dict[kv.Key] = kv.Value;
                }
            }
        }

        // Overlay global preferences (context, formality)
        var opts = Toucan.Core.Options.AppOptions.LoadFromDisk();
        if (!string.IsNullOrWhiteSpace(opts.Context))
        {
            dict["context"] = opts.Context;
        }

        if (!string.IsNullOrWhiteSpace(opts.Formality) && opts.Formality != "Default")
        {
            dict["formality"] = opts.Formality.ToLowerInvariant();
        }

        return dict;
    }
}
