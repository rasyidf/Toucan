using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// First-run setup. Today it has one decision, whether to use AI features (off unless the user turns it on), and
/// optionally the AI service and key. Shown once; <see cref="Toucan.Core.Options.AppOptions.OnboardingVersion"/> records it.
/// </summary>
public partial class OnboardingViewModel(AiSettingsViewModel ai) : ObservableObject
{
    /// <summary>Bump when a new onboarding step is added, so existing users see it once.</summary>
    public const int CurrentVersion = 1;

    public AiSettingsViewModel Ai { get; } = ai;

    /// <summary>The user's choice. Off by default: nothing is sent to an AI service unless they turn it on.</summary>
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
        Ai.Enabled = UseAi;
        Ai.Save();
        CloseAction?.Invoke(true);
    }

    /// <summary>The window was closed without Continue: AI stays off, and the choice is saved so the question is not asked again.</summary>
    public void Dismiss()
    {
        Ai.Enabled = false;
        Ai.Save();
    }
}
