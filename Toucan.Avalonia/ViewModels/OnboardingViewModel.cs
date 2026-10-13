using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// First-run setup: the default source language and theme, and whether to use AI features (off unless the user turns it
/// on), with an optional AI service and key. Shown once; <see cref="Toucan.Core.Options.AppOptions.OnboardingVersion"/>
/// records it, so bumping <see cref="CurrentVersion"/> shows the new steps to existing users once.
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    /// <summary>Bump when a new onboarding step is added, so existing users see it once. v2 added language and theme.</summary>
    public const int CurrentVersion = 2;

    private readonly IPreferenceService? _preferences;

    public OnboardingViewModel(AiSettingsViewModel ai, IPreferenceService? preferences = null)
    {
        Ai = ai;
        _preferences = preferences;
        var options = preferences?.Load();
        DefaultLanguage = options?.DefaultLanguage ?? "en-US";
        Theme = options?.Theme ?? "System";
    }

    public AiSettingsViewModel Ai { get; }

    /// <summary>The default source language for new projects.</summary>
    [ObservableProperty] private string defaultLanguage = "en-US";

    /// <summary>Common source languages offered in onboarding; any language can still be set later in Settings.</summary>
    public IReadOnlyList<string> LanguageChoices { get; } = ["en-US", "en-GB", "fr-FR", "de-DE", "es-ES", "it-IT", "pt-BR", "id-ID", "ja-JP", "zh-CN"];

    /// <summary>System / Light / Dark.</summary>
    [ObservableProperty] private string theme = "System";

    public IReadOnlyList<string> ThemeChoices { get; } = ["System", "Light", "Dark"];

    /// <summary>The user's AI choice. Off by default: nothing is sent to an AI service unless they turn it on.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(KeepAiOff))] private bool useAi;

    public bool KeepAiOff
    {
        get => !UseAi;
        set => UseAi = !value;
    }

    public Action<bool>? CloseAction { get; set; }

    [RelayCommand]
    private void Finish()
    {
        SavePreferences();
        Ai.Enabled = UseAi;
        Ai.Save();
        CloseAction?.Invoke(true);
    }

    /// <summary>The window was closed without Continue: AI stays off, but the language/theme chosen are kept, and the choice is saved so the question is not asked again.</summary>
    public void Dismiss()
    {
        SavePreferences();
        Ai.Enabled = false;
        Ai.Save();
    }

    private void SavePreferences()
    {
        if (_preferences is null) return;
        var options = _preferences.Load();
        options.DefaultLanguage = DefaultLanguage;
        options.Theme = Theme;
        _preferences.Save(options);
        ThemeService.Apply(Theme);
    }
}
