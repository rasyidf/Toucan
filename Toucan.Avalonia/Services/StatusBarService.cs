using System;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Services;

public class StatusBarService
{
    private StatusBarViewModel? _vm;
    private static StatusBarService? _instance;
    public static StatusBarService Instance => _instance ??= new StatusBarService();

    private StatusBarService() { }

    public void Register(StatusBarViewModel vm) => _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    public void Unregister() => _vm = null;
    public void SetLoading(bool isLoading) { if (_vm != null) _vm.IsLoading = isLoading; }
    public void UpdateStatus(string text) { if (_vm != null) _vm.StatusText = text; }
    public void UpdateProjectName(string name) { if (_vm != null) _vm.ProjectName = name; }
    public void UpdateCursor(string pos) { if (_vm != null) _vm.CursorPosition = pos; }
    public void UpdateDefaultLanguage(string lang) { if (_vm != null) _vm.DefaultLanguage = lang; }
    public void ShowNotificationBadge(int count) { if (_vm != null) _vm.NotificationCount = count; }
}
