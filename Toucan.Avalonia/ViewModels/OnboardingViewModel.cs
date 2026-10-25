using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// First-run language, theme and optional AI setup. Shown once per onboarding version.
/// </summary>
public partial class OnboardingViewModel : ObservableObject
{
    /// <summary>Bump when a new onboarding step is added, so existing users see it once.</summary>
    public const int CurrentVersion = 2;

    private readonly Toucan.Core.Contracts.IPreferenceService? _preferences;
    public AiSettingsViewModel Ai { get; }
    public OnboardingViewModel(AiSettingsViewModel ai, Toucan.Core.Contracts.IPreferenceService? preferences = null)
    {
        Ai = ai; _preferences = preferences;
        var options = preferences?.Load();
        defaultLanguage = options?.DefaultLanguage ?? "en-US";
        theme = options?.Theme ?? "System";
        useAi = ai.Enabled;
    }

    public IReadOnlyList<string> LanguageChoices { get; } = ["en-US", "en-GB", "fr-FR", "de-DE", "es-ES", "it-IT", "pt-BR", "id-ID", "ja-JP", "zh-CN"];
    public IReadOnlyList<string> ThemeChoices => Themes;
    public static IReadOnlyList<string> Themes { get; } = ["System", "Light", "Dark"];
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(FinishCommand))] private string defaultLanguage;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(FinishCommand))] private string theme;
    private bool CanFinish() => !string.IsNullOrWhiteSpace(DefaultLanguage) && DefaultLanguage.Length <= 64
        && !DefaultLanguage.Any(char.IsWhiteSpace) && Themes.Contains(Theme);

    /// <summary>The user's choice. Off by default: nothing is sent to an AI service unless they turn it on.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(KeepAiOff))] private bool useAi;

    public bool KeepAiOff
    {
        get => !UseAi;
        set => UseAi = !value;
    }

    public Action<bool>? CloseAction { get; set; }

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Finish()
    {
        if (!CanFinish()) return;
        Ai.Enabled = UseAi;
        Ai.Save();
        if (_preferences != null)
        {
            var options = _preferences.Load();
            options.DefaultLanguage = DefaultLanguage.Trim(); options.Theme = Theme;
            options.OnboardingVersion = CurrentVersion;
            _preferences.Save(options);
        }
        Toucan.Avalonia.Services.ThemeService.Apply(Theme);
        CloseAction?.Invoke(true);
    }

    /// <summary>The window was closed without Continue: AI stays off, and the choice is saved so the question is not asked again.</summary>
    public void Dismiss()
    {
        Ai.Enabled = false;
        Ai.Save();
    }
}
