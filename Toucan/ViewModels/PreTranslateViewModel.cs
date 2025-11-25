using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

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
    private string selectedProvider = "Google";

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } = new();

    [ObservableProperty]
    private bool resetApproved = true;

    [ObservableProperty]
    private bool translateSelectedOnly = true;

    [ObservableProperty]
    private bool overwriteExisting = false;

    private readonly IPretranslationService? _pretranslationService;
    private readonly IEnumerable<TranslationItem>? _sourceItems;

    public PreTranslateViewModel()
    {
        // default languages for preview
        AvailableLanguages.Add(new LanguageItem("id-ID", true));
        AvailableLanguages.Add(new LanguageItem("zh-CN", true));
        AvailableLanguages.Add(new LanguageItem("fr-FR", false));
    }

    public PreTranslateViewModel(IEnumerable<string> languages, IEnumerable<TranslationItem>? sourceItems = null, IPretranslationService? pretranslation = null)
    {
        _pretranslationService = pretranslation;
        _sourceItems = sourceItems;

        foreach (var l in languages)
            AvailableLanguages.Add(new LanguageItem(l, true));
    }

    [RelayCommand]
    private void Cancel()
    {
        // handled by window code-behind
    }

    [RelayCommand]
    private async Task Start()
    {
        // Gather request and call pretranslation service if available
        if (_pretranslationService == null || _sourceItems == null)
        {
            // Nothing to do in this simplified flow
            return;
        }

        var selectedLanguages = AvailableLanguages.Where(l => l.IsSelected).Select(l => l.Name).ToHashSet(StringComparer.InvariantCultureIgnoreCase);

        IEnumerable<TranslationItem> itemsToProcess = _sourceItems;
        if (TranslateSelectedOnly)
            itemsToProcess = itemsToProcess.Where(i => selectedLanguages.Contains(i.Language));

        var req = new PretranslationRequest
        {
            Provider = SelectedProvider,
            Items = itemsToProcess.ToList(),
            Options = new PretranslationOptions { Overwrite = OverwriteExisting }
        };

        await _pretranslationService.PreTranslateAsync(req).ConfigureAwait(false);
    }

    [RelayCommand]
    private void OpenProviderSettings()
    {
        // placeholder for opening provider settings (hook into app's dialog service later)
    }
}
