using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Models;

namespace Toucan.ViewModels.StatusBarPanels;

// ═══════════════════════════════════════════════════════════════════════
// VCS PANEL — branch, changes, sync status
// ═══════════════════════════════════════════════════════════════════════

public partial class VcsPanel : StatusBarPanelBase
{
    public override string Id => "vcs";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Left;

    [ObservableProperty]
    private string branchName = "";

    [ObservableProperty]
    private int changedFiles;

    [ObservableProperty]
    private int ahead;

    [ObservableProperty]
    private int behind;

    public ObservableCollection<string> ChangesSummary { get; } = [];

    public override ICommand? ClickCommand => ShowDetailsCommand;

    [RelayCommand]
    private void ShowDetails() { /* ponytail: flyout handled by view */ }

    public void Update(string branch, int changes, int ahead = 0, int behind = 0, IEnumerable<string>? summary = null)
    {
        BranchName = branch;
        ChangedFiles = changes;
        Ahead = ahead;
        Behind = behind;
        IsVisible = !string.IsNullOrEmpty(branch);

        Icon = "BranchFork24";
        Content = string.IsNullOrEmpty(branch) ? "" : $"{branch}";
        Badge = changes > 0 ? changes.ToString() : null;
        BadgeSeverity = changes > 0 ? "warning" : null;

        var syncText = (ahead, behind) switch
        {
            ( > 0, > 0) => $" ↑{ahead} ↓{behind}",
            ( > 0, _) => $" ↑{ahead}",
            (_, > 0) => $" ↓{behind}",
            _ => ""
        };
        ToolTip = $"Branch: {branch}\n{changes} changed file(s){syncText}";

        ChangesSummary.Clear();
        if (summary != null)
            foreach (var line in summary)
                ChangesSummary.Add(line);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// TRANSLATION STATS PANEL — translated/total + errors/warnings
// ═══════════════════════════════════════════════════════════════════════

public partial class TranslationStatsPanel : StatusBarPanelBase
{
    public override string Id => "stats";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Left;

    [ObservableProperty]
    private int totalKeys;

    [ObservableProperty]
    private int translatedKeys;

    [ObservableProperty]
    private int errors;

    [ObservableProperty]
    private int warnings;

    public ObservableCollection<SummaryItem> LanguageStats { get; } = [];

    public override ICommand? ClickCommand => ShowStatisticsCommand;

    [RelayCommand]
    private void ShowStatistics() { /* ponytail: triggers statistics dialog via event */ }

    /// <summary>Event raised when panel is clicked to open statistics dialog.</summary>
#pragma warning disable CS0067 // Event never used — reserved for future view wiring
    public event EventHandler? StatisticsRequested;
#pragma warning restore CS0067

    public void Update(int total, int translated, int errorCount, int warningCount, IEnumerable<SummaryItem>? perLanguage = null)
    {
        TotalKeys = total;
        TranslatedKeys = translated;
        Errors = errorCount;
        Warnings = warningCount;

        Icon = "Translate24";
        Content = total == 0 ? "No keys" : $"{translated}/{total}";

        // Show worst badge
        if (errorCount > 0) { Badge = errorCount.ToString(); BadgeSeverity = "error"; }
        else if (warningCount > 0) { Badge = warningCount.ToString(); BadgeSeverity = "warning"; }
        else { Badge = null; BadgeSeverity = null; }

        var pct = total == 0 ? 0 : (int)(translated * 100.0 / total);
        ToolTip = $"Translation Progress: {pct}%\n{translated}/{total} keys translated\n{errorCount} error(s), {warningCount} warning(s)";

        LanguageStats.Clear();
        if (perLanguage != null)
            foreach (var item in perLanguage)
                LanguageStats.Add(item);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// MODE PANEL — EDITOR / REVIEW / AUDIT badge
// ═══════════════════════════════════════════════════════════════════════

public partial class ModePanel : StatusBarPanelBase
{
    public override string Id => "mode";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Left;

    [ObservableProperty]
    private string modeName = "EDITOR";

    public override ICommand? ClickCommand => CycleModeCommand;

    [RelayCommand]
    private void CycleMode() { /* ponytail: handled by view binding to PanelService */ }

    public void Update(string mode)
    {
        ModeName = mode.ToUpperInvariant();
        Content = ModeName;
        Icon = null; // badge-style, no icon
        ToolTip = $"Editor Mode: {ModeName}\nClick to cycle";
    }
}

// ═══════════════════════════════════════════════════════════════════════
// PROJECT PANEL — project name + dirty indicator
// ═══════════════════════════════════════════════════════════════════════

public partial class ProjectPanel : StatusBarPanelBase
{
    public override string Id => "project";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Left;

    [ObservableProperty]
    private string projectName = "Toucan Project";

    [ObservableProperty]
    private int dirtyCount;

    public override ICommand? ClickCommand => OpenProjectPropertiesCommand;

    [RelayCommand]
    private void OpenProjectProperties() { /* ponytail: triggers project properties dialog */ }

#pragma warning disable CS0067
    public event EventHandler? ProjectPropertiesRequested;
#pragma warning restore CS0067

    public void Update(string name, int dirty = 0)
    {
        ProjectName = name;
        DirtyCount = dirty;
        Icon = "Home24";
        Content = name;
        Badge = dirty > 0 ? $"● {dirty}" : null;
        BadgeSeverity = dirty > 0 ? "warning" : null;
        ToolTip = dirty > 0 ? $"{name}\n{dirty} unsaved change(s)" : name;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// STATUS PANEL — ephemeral status text (center)
// ═══════════════════════════════════════════════════════════════════════

public partial class StatusPanel : StatusBarPanelBase
{
    public override string Id => "status";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Center;

    public void Update(string text)
    {
        Content = text;
        ToolTip = null;
        Icon = null;
        Badge = null;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// LANGUAGE PANEL — primary language with switcher
// ═══════════════════════════════════════════════════════════════════════

public partial class LanguagePanel : StatusBarPanelBase
{
    public override string Id => "language";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Right;

    [ObservableProperty]
    private string currentLanguage = "en-US";

    public ObservableCollection<string> AvailableLanguages { get; } = [];

    public override ICommand? ClickCommand => SwitchLanguageCommand;

    [RelayCommand]
    private void SwitchLanguage() { /* ponytail: opens context menu in view */ }

#pragma warning disable CA1003
    public event Action<string>? LanguageChanged;
#pragma warning restore CA1003

    public void Update(string language, IEnumerable<string>? available = null)
    {
        CurrentLanguage = language;
        Content = language;
        Icon = "LocalLanguage24";
        ToolTip = $"Primary Language: {language}\nClick to switch";

        if (available != null)
        {
            AvailableLanguages.Clear();
            foreach (var lang in available)
                AvailableLanguages.Add(lang);
        }
    }

    public void SetLanguage(string lang)
    {
        CurrentLanguage = lang;
        Content = lang;
        LanguageChanged?.Invoke(lang);
    }
}

// ═══════════════════════════════════════════════════════════════════════
// ENCODING PANEL — file encoding display
// ═══════════════════════════════════════════════════════════════════════

public partial class EncodingPanel : StatusBarPanelBase
{
    public override string Id => "encoding";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Right;

    [ObservableProperty]
    private string encoding = "UTF-8";

    public override ICommand? ClickCommand => ChangeEncodingCommand;

    [RelayCommand]
    private void ChangeEncoding() { /* ponytail: future — encoding selection popup */ }

    public void Update(string enc)
    {
        Encoding = enc;
        Content = enc;
        ToolTip = $"File Encoding: {enc}";
    }
}

// ═══════════════════════════════════════════════════════════════════════
// LINE ENDINGS PANEL — LF / CRLF
// ═══════════════════════════════════════════════════════════════════════

public partial class LineEndingsPanel : StatusBarPanelBase
{
    public override string Id => "eol";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Right;

    [ObservableProperty]
    private string lineEnding = "LF";

    public override ICommand? ClickCommand => ToggleLineEndingCommand;

    [RelayCommand]
    private void ToggleLineEnding()
    {
        LineEnding = LineEnding == "LF" ? "CRLF" : "LF";
        Content = LineEnding;
    }

    public void Update(string eol)
    {
        LineEnding = eol;
        Content = eol;
        ToolTip = $"Line Endings: {(eol == "LF" ? "Unix (LF)" : "Windows (CRLF)")}";
    }
}

// ═══════════════════════════════════════════════════════════════════════
// NOTIFICATIONS PANEL — badge with count
// ═══════════════════════════════════════════════════════════════════════

public partial class NotificationsPanel : StatusBarPanelBase
{
    public override string Id => "notifications";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Right;

    [ObservableProperty]
    private int count;

    public override ICommand? ClickCommand => ShowNotificationsCommand;

    [RelayCommand]
    private void ShowNotifications() { /* ponytail: opens notification flyout */ }

    public void Update(int notificationCount)
    {
        Count = notificationCount;
        IsVisible = notificationCount > 0;
        Content = "";
        Icon = "Alert24";
        Badge = notificationCount > 0 ? notificationCount.ToString() : null;
        BadgeSeverity = null; // accent color
        ToolTip = $"{notificationCount} notification(s)";
    }
}

// ═══════════════════════════════════════════════════════════════════════
// LOADING PANEL — spinner for async operations
// ═══════════════════════════════════════════════════════════════════════

public partial class LoadingPanel : StatusBarPanelBase
{
    public override string Id => "loading";
    public override StatusBarAlignment Alignment => StatusBarAlignment.Right;

    [ObservableProperty]
    private bool isActive;

    public void Update(bool loading)
    {
        IsActive = loading;
        IsVisible = loading;
        Content = "";
        ToolTip = loading ? "Processing..." : null;
    }
}
