using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.Services;
using Toucan.Core.Commands;

namespace Toucan.Avalonia.ViewModels;

/// <summary>One command in the Shortcuts settings page: its current shortcut and the state of an edit in progress.</summary>
/// <summary>What a shortcut row asks of the page that owns it.</summary>
public interface IShortcutEditor
{
    void Change(ShortcutRowViewModel row);
    void Reset(ShortcutRowViewModel row);
    void Clear(ShortcutRowViewModel row);
}

public sealed partial class ShortcutRowViewModel : ObservableObject
{
    private readonly ICommandRegistry _registry;

    public ShortcutRowViewModel(ICommandRegistry registry, string commandId)
    {
        _registry = registry;
        CommandId = commandId;
        Category = Loc.T(registry.GetCategory(commandId));
        Title = Loc.T(registry.GetTitle(commandId));
        Refresh();
    }

    /// <summary>The page this row belongs to; set once the page is built.</summary>
    public IShortcutEditor? Editor { get; set; }

    [RelayCommand] private void Change() => Editor?.Change(this);
    [RelayCommand] private void Reset() => Editor?.Reset(this);
    [RelayCommand] private void Clear() => Editor?.Clear(this);

    public string CommandId { get; }
    public string Category { get; }
    public string Title { get; }

    [ObservableProperty] private string shortcut = string.Empty;
    [ObservableProperty] private bool hasShortcut;
    [ObservableProperty] private bool isCustom;
    [ObservableProperty] private bool isCapturing;

    /// <summary>Why the last attempt did not apply (a conflict or an unusable key); empty when there is nothing to say.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasMessage))] private string message = string.Empty;

    /// <summary>The shortcut waiting for a second press to take it over from the commands that use it.</summary>
    public string? PendingShortcut { get; set; }

    public bool HasMessage => Message.Length > 0;

    /// <summary>What the badge shows: the shortcut, a prompt while capturing, or "Not set".</summary>
    public string BadgeText => IsCapturing ? Loc.T("Press keys…") : HasShortcut ? Shortcut : Loc.T("Not set");

    /// <summary>The button text: "Change", or "Cancel" while this row waits for keys.</summary>
    public string ChangeText => Loc.T(IsCapturing ? "Cancel" : "Change");

    partial void OnShortcutChanged(string value) => OnPropertyChanged(nameof(BadgeText));
    partial void OnHasShortcutChanged(bool value) => OnPropertyChanged(nameof(BadgeText));
    partial void OnIsCapturingChanged(bool value)
    {
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(ChangeText));
    }

    public void Refresh()
    {
        var gesture = KeybindingService.ToGesture(_registry.GetShortcut(CommandId));
        HasShortcut = gesture is not null;
        Shortcut = gesture is null ? string.Empty : KeybindingService.Display(gesture);
        IsCustom = _registry.CustomShortcuts.ContainsKey(CommandId);
    }
}

public sealed record ShortcutEditGroup(string Category, IReadOnlyList<ShortcutRowViewModel> Rows);
