using System;
using System.Collections.Generic;
using Toucan.Core.Models;
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
    void UpdateDefaultLanguage(string language);
    void ShowNotificationBadge(int count);
    void UpdateSourceControl(string branch, int changes, IEnumerable<string>? summary = null);
    void UpdateStatistics(int totalKeys, int translated, int errors, int warnings, IEnumerable<SummaryItem>? perLanguage = null);
}

internal class StatusBarService : IStatusBarService
{
    private StatusBarViewModel? _vm;

    public static StatusBarService Instance => field ??= new StatusBarService();

    private StatusBarService() { }

    public void Register(StatusBarViewModel vm) => _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    public void Unregister() => _vm = null;
    public StatusBarViewModel? GetViewModel() => _vm;

    public void SetLoading(bool isLoading) { if (_vm != null) _vm.IsLoading = isLoading; }
    public void UpdateStatus(string text) { if (_vm != null) _vm.StatusText = text; }
    public void UpdateProjectName(string name) { if (_vm != null) _vm.ProjectName = name; }
    public void UpdateCursor(string pos) { if (_vm != null) _vm.CursorPosition = pos; }
    public void UpdateDefaultLanguage(string lang) { if (_vm != null) _vm.DefaultLanguage = lang; }
    public void ShowNotificationBadge(int count) { if (_vm != null) _vm.ShowNotification(count); }

    public void UpdateSessionDirtyCount(int count) { if (_vm != null) _vm.SessionDirtyCount = count; }

    public void UpdateSourceControl(string branch, int changes, IEnumerable<string>? summary = null)
    {
        _vm?.UpdateSourceControl(branch, changes, summary);
    }

    public void UpdateStatistics(int totalKeys, int translated, int errors, int warnings, IEnumerable<SummaryItem>? perLanguage = null)
    {
        _vm?.UpdateStatistics(totalKeys, translated, errors, warnings, perLanguage);
    }

    // ponytail: legacy shim kept to avoid breaking existing callers during migration
    public void UpdateGitStats(int changes, int errors, int warnings)
    {
        _vm?.UpdateSourceControl(_vm.BranchName, changes);
        _vm?.UpdateStatistics(_vm.TotalKeys, _vm.TranslatedKeys, errors, warnings);
    }
}
