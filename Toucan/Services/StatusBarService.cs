using System;
using Toucan.ViewModels;

namespace Toucan.Services;

internal interface IStatusBarService
{
    void Register(StatusBarViewModel vm);
    void Unregister();
    void SetLoading(bool isLoading);
    void UpdateStatus(string text);
    void UpdateProjectName(string projectName);
    void UpdateCursor(string cursorPosition);
    void ShowNotificationBadge(int count);
    void UpdateGitStats(int changes, int errors, int warnings);
}

internal class StatusBarService : IStatusBarService
{
    private StatusBarViewModel _statusBarViewModel;

    // Simple singleton-style access (keep scoped for the app)
    private static StatusBarService? _instance;
    public static StatusBarService Instance => _instance ??= new StatusBarService();

    private StatusBarService()
    {
    }

    public void Register(StatusBarViewModel vm)
    {
        _statusBarViewModel = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public void Unregister()
    {
        _statusBarViewModel = null;
    }

    public void SetLoading(bool isLoading)
    {
        if (_statusBarViewModel == null) return;
        _statusBarViewModel.IsLoading = isLoading;
    }

    public void UpdateStatus(string text)
    {
        if (_statusBarViewModel == null) return;
        _statusBarViewModel.StatusText = text;
    }

    public void UpdateProjectName(string projectName)
    {
        if (_statusBarViewModel == null) return;
        _statusBarViewModel.ProjectName = projectName;
    }

    public void UpdateCursor(string cursorPosition)
    {
        if (_statusBarViewModel == null) return;
        _statusBarViewModel.CursorPosition = cursorPosition;
    }

    public void ShowNotificationBadge(int count)
    {
        if (_statusBarViewModel == null) return;
        _statusBarViewModel.ShowNotification(count);
    }

    public void UpdateGitStats(int changes, int errors, int warnings)
    {
        if (_statusBarViewModel == null) return;
        _statusBarViewModel.UpdateGitStats(changes, errors, warnings);

        // TODO: Add IoC or event aggregator notifications so other modules can update the status bar.
        // TODO: Integrate with GitService or libgit2sharp to get branch, changed files, ahead/behind, etc.
    }
}
