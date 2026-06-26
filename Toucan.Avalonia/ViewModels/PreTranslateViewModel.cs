using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public partial class LanguageItem : ObservableObject
{
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private bool isSelected;
    public LanguageItem(string name, bool isSelected = false) { Name = name; IsSelected = isSelected; }
}

public partial class PreTranslateViewModel : ObservableObject
{
    [ObservableProperty] private string selectedProvider = "Google";
    [ObservableProperty] private bool resetApproved = true;
    [ObservableProperty] private bool translateSelectedOnly = true;
    [ObservableProperty] private bool overwriteExisting;
    [ObservableProperty] private int progressCompleted;
    [ObservableProperty] private int progressTotal;
    [ObservableProperty] private bool isRunning;

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } = new();
    public ObservableCollection<PretranslationItemResult> PreviewResults { get; } = new();
    public double ProgressPercent => ProgressTotal == 0 ? 0 : Math.Clamp((double)ProgressCompleted / ProgressTotal * 100, 0, 100);

    private readonly IPretranslationService? _pretranslationService;
    private readonly IEnumerable<TranslationItem>? _sourceItems;
    private CancellationTokenSource? _cts;

    public PreTranslateViewModel() { AvailableLanguages.Add(new LanguageItem("en-US", true)); }

    public PreTranslateViewModel(IEnumerable<string> languages, IEnumerable<TranslationItem>? sourceItems = null, IPretranslationService? pretranslation = null)
    {
        _pretranslationService = pretranslation;
        _sourceItems = sourceItems;
        foreach (var l in languages) AvailableLanguages.Add(new LanguageItem(l, true));
    }

    [RelayCommand]
    private async Task Start()
    {
        if (_pretranslationService == null || _sourceItems == null) return;
        var selected = AvailableLanguages.Where(l => l.IsSelected).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = TranslateSelectedOnly ? _sourceItems.Where(i => selected.Contains(i.Language)) : _sourceItems;
        PreviewResults.Clear();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ProgressCompleted = 0; ProgressTotal = 0;
        var progress = new Progress<PretranslationProgress>(p => { ProgressCompleted = p.Completed; ProgressTotal = p.Total; OnPropertyChanged(nameof(ProgressPercent)); });
        var req = new PretranslationRequest { Provider = SelectedProvider, Items = items.ToList(), ContextItems = _sourceItems, Options = new PretranslationOptions { Overwrite = OverwriteExisting, PreviewOnly = true } };
        var result = await _pretranslationService.PreTranslateAsync(req, progress, _cts.Token);
        foreach (var r in result.Items) PreviewResults.Add(r);
        IsRunning = false;
    }

    [RelayCommand] private void Cancel() { _cts?.Cancel(); }

    [RelayCommand]
    private void Commit()
    {
        if (PreviewResults == null || _sourceItems == null) return;
        var items = _sourceItems.ToList();
        foreach (var pr in PreviewResults)
        {
            if (string.IsNullOrEmpty(pr.TranslatedValue)) continue;
            var item = items.FirstOrDefault(i => i.Namespace == pr.Namespace && i.Language == pr.Language);
            if (item == null) continue;
            if (OverwriteExisting || string.IsNullOrEmpty(item.Value)) item.Value = pr.TranslatedValue;
        }
    }
}
