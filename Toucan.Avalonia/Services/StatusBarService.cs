using Toucan.Avalonia.ViewModels;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Services;

/// <summary>
/// App-wide access point for status bar updates. The main window registers the shared
/// <see cref="StatusBarViewModel"/> at startup; calls before registration are no-ops.
/// </summary>
public sealed class StatusBarService
{
    private StatusBarViewModel? _vm;

    public static StatusBarService Instance { get; } = new();

    private StatusBarService() { }

    public void Register(StatusBarViewModel vm) => _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    public void Unregister() => _vm = null;
    public StatusBarViewModel? ViewModel => _vm;


    public void SetLoading(bool isLoading) { if (_vm != null) _vm.IsLoading = isLoading; }
    public void UpdateStatus(string text) { if (_vm != null) _vm.StatusText = text; }
    public void UpdateProjectName(string name) { if (_vm != null) _vm.ProjectName = name; }
    public void UpdateCursor(string pos) { if (_vm != null) _vm.CursorPosition = pos; }
    public void UpdateDefaultLanguage(string lang) { if (_vm != null) _vm.DefaultLanguage = lang; }
    public void ShowNotificationBadge(int count) { _vm?.ShowNotification(count); }
    public void UpdateSessionDirtyCount(int count) { if (_vm != null) _vm.SessionDirtyCount = count; }
    public void UpdateEditorMode(string mode) { _vm?.Mode.Update(mode); }

    public void UpdateStatistics(int totalKeys, int translated, int errors, int warnings, IEnumerable<SummaryItem>? perLanguage = null)
        => _vm?.UpdateStatistics(totalKeys, translated, errors, warnings, perLanguage);
}
