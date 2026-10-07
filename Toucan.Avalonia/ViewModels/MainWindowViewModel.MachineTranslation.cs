using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Services.Ai;

namespace Toucan.Avalonia.ViewModels;

/// <summary>A provider offered in the Machine Translation panel.</summary>
public partial class ProviderChoice(string name, string displayName, string description, bool isConfigured) : ObservableObject
{
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName;
    public string Description { get; } = description;

    /// <summary>True when every secret the provider declares (API key, token) has a value.</summary>
    public bool IsConfigured { get; } = isConfigured;

    [ObservableProperty] private bool isSelected;
}

/// <summary>One value from the most recent machine translation run.</summary>
public sealed record TranslationRunItem(string Key, string Language, string Text, bool Succeeded)
{
    public string Heading => $"{Key} · {Language}";
}

/// <summary>State and commands behind the Machine Translation side panel.</summary>
public partial class MainWindowViewModel
{
    private const int MaxRecentTranslations = 200;

    public ObservableCollection<ProviderChoice> ProviderChoices { get; } = [];
    public ObservableCollection<TranslationRunItem> RecentTranslations { get; } = [];

    /// <summary>The provider quick translate commands use.</summary>
    public string SelectedProviderName => ResolveProvider();

    public bool HasRecentTranslations => RecentTranslations.Count > 0;
    public bool HasNoRecentTranslations => RecentTranslations.Count == 0;

    [ObservableProperty] private string lastTranslationSummary = string.Empty;

    /// <summary>
    /// The last-used provider if it is still available, otherwise the first one listed. Providers without a
    /// settings definition (such as Mock) are not listed but stay usable when they were the last choice.
    /// </summary>
    private string ResolveProvider()
    {
        var known = UsableProviderNames();
        // Claude, OpenAI and Gemini were providers before AI Integration; they are the AI provider now.
        var last = LegacyAiMigration.CurrentProviderName(AppOptions.LastProvider);
        return known.FirstOrDefault(n => string.Equals(n, last, StringComparison.OrdinalIgnoreCase))
            ?? (ProviderChoices.Count > 0 ? ProviderChoices[0].Name : known[0]);
    }

    /// <summary>Listed providers first, then the built-in names not listed (Mock), without duplicates.</summary>
    private List<string> UsableProviderNames() =>
        [.. ProviderChoices.Select(c => c.Name).Concat(PreTranslateViewModel.Providers).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => IsAiEnabled || !IsAiProvider(n))];

    private static bool IsAiProvider(string name) => string.Equals(name, AiFeatureIds.TranslationProviderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rebuilds the provider list from the registry (so plugin providers appear) and re-checks which have credentials.</summary>
    internal void RefreshProviderChoices()
    {
        ProviderChoices.Clear();
        var definitions = _providerRegistry.GetAll();
        foreach (var def in definitions)
        {
            // The AI provider is only offered while AI is on, and is ready when AI Integration has a usable service.
            if (IsAiProvider(def.Name))
            {
                if (!IsAiEnabled) continue;
                ProviderChoices.Add(new ProviderChoice(def.Name, def.DisplayName, def.Description, _ai.GetStatus().IsReady));
                continue;
            }
            var options = ProviderOptionsBuilder.Build(def.Name, _providerSettingsService, string.IsNullOrEmpty(CurrentPath) ? null : CurrentPath);
            var configured = def.SecretFields.Keys.All(k => options.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v));
            ProviderChoices.Add(new ProviderChoice(def.Name, string.IsNullOrWhiteSpace(def.DisplayName) ? def.Name : def.DisplayName, def.Description, configured));
        }

        var selected = ResolveProvider();
        foreach (var c in ProviderChoices) c.IsSelected = string.Equals(c.Name, selected, StringComparison.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(SelectedProviderName));
    }

    /// <summary>Remembers a provider picked elsewhere (the Pre-translate dialog) so the panel and quick translate follow it.</summary>
    private void RememberProvider(string name)
    {
        if (string.Equals(name, AppOptions.LastProvider, StringComparison.OrdinalIgnoreCase)) return;
        AppOptions.LastProvider = name;
        _preferenceService.Save(AppOptions);
        RefreshProviderChoices();
    }

    [RelayCommand]
    private void SelectProvider(ProviderChoice? choice)
    {
        if (choice == null) return;
        AppOptions.LastProvider = choice.Name;
        _preferenceService.Save(AppOptions);
        foreach (var c in ProviderChoices) c.IsSelected = ReferenceEquals(c, choice);
        OnPropertyChanged(nameof(SelectedProviderName));
        StatusText = choice.IsConfigured
            ? $"Machine translation provider: {choice.DisplayName}."
            : IsAiProvider(choice.Name)
                ? $"AI is not set up yet: {_ai.GetStatus().Problem}"
                : $"{choice.DisplayName} has no API key yet. Add it under Translation Providers.";
    }

    [RelayCommand]
    private async Task OpenProviderSettings()
    {
        // The AI provider has no provider settings: its service, key and prompt are under Settings → AI.
        if (IsAiProvider(SelectedProviderName))
        {
            await OpenAiSettings();
            return;
        }
        await _dialogService.ShowProviderSettingsAsync(HasProject ? CurrentPath : null);
        RefreshProviderChoices();
    }

    [RelayCommand]
    private void ClearRecentTranslations()
    {
        RecentTranslations.Clear();
        LastTranslationSummary = string.Empty;
        NotifyRecentTranslationsChanged();
    }

    /// <summary>Records a quick translate run so the panel can show what the provider returned.</summary>
    private void RecordTranslationRun(string summary, IEnumerable<PretranslationItemResult> results)
    {
        RecentTranslations.Clear();
        foreach (var r in results.Take(MaxRecentTranslations))
            RecentTranslations.Add(new TranslationRunItem(r.Namespace, r.Language, r.Succeeded ? r.TranslatedValue ?? string.Empty : r.ErrorMessage ?? "Failed", r.Succeeded));
        LastTranslationSummary = summary;
        NotifyRecentTranslationsChanged();
    }

    private void NotifyRecentTranslationsChanged()
    {
        OnPropertyChanged(nameof(HasRecentTranslations));
        OnPropertyChanged(nameof(HasNoRecentTranslations));
    }
}
