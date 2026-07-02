using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.ViewModels.StatusBarPanels;

namespace Toucan.ViewModels;

/// <summary>
/// Modular status bar ViewModel backed by the panel registry.
/// Exposes typed panel accessors and delegates updates to individual panels.
/// Backward-compatible: existing callers can still set StatusText, IsLoading, etc.
/// </summary>
internal partial class StatusBarViewModel : ObservableObject
{
    public StatusBarPanelRegistry Registry { get; } = StatusBarPanelRegistry.Instance;

    // ═══ Typed panel accessors ═══
    public VcsPanel Vcs { get; }
    public TranslationStatsPanel Stats { get; }
    public ModePanel Mode { get; }
    public ProjectPanel Project { get; }
    public StatusPanel Status { get; }
    public LanguagePanel Language { get; }
    public EncodingPanel Encoding { get; }
    public LineEndingsPanel LineEndings { get; }
    public NotificationsPanel Notifications { get; }
    public LoadingPanel Loading { get; }

    public StatusBarViewModel()
    {
        // Create and register built-in panels with default order
        Vcs = new VcsPanel { Order = 10 };
        Stats = new TranslationStatsPanel { Order = 20 };
        Mode = new ModePanel { Order = 30 };
        Project = new ProjectPanel { Order = 40 };
        Status = new StatusPanel { Order = 50 };
        Language = new LanguagePanel { Order = 10 };
        Encoding = new EncodingPanel { Order = 20 };
        LineEndings = new LineEndingsPanel { Order = 30 };
        Notifications = new NotificationsPanel { Order = 40 };
        Loading = new LoadingPanel { Order = 50 };

        Registry.Register(Vcs);
        Registry.Register(Stats);
        Registry.Register(Mode);
        Registry.Register(Project);
        Registry.Register(Status);
        Registry.Register(Language);
        Registry.Register(Encoding);
        Registry.Register(LineEndings);
        Registry.Register(Notifications);
        Registry.Register(Loading);

        // Default state
        Mode.Update("Editor");
        Encoding.Update("UTF-8");
        LineEndings.Update("LF");
    }

    // ═══ Backward-compatible properties (delegate to panels) ═══

    public string StatusText
    {
        get => Status.Content;
        set { Status.Update(value); OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => Loading.IsActive;
        set { Loading.Update(value); OnPropertyChanged(); }
    }

    public string ProjectName
    {
        get => Project.ProjectName;
        set { Project.Update(value, Project.DirtyCount); OnPropertyChanged(); }
    }

    public int SessionDirtyCount
    {
        get => Project.DirtyCount;
        set { Project.Update(Project.ProjectName, value); OnPropertyChanged(); }
    }

    public string DefaultLanguage
    {
        get => Language.CurrentLanguage;
        set { Language.Update(value); OnPropertyChanged(); }
    }

    public int NotificationCount
    {
        get => Notifications.Count;
        set { Notifications.Update(value); OnPropertyChanged(); }
    }

    // Cursor position (not a panel — inline text display)
    public string CursorPosition
    {
        get => _cursorPosition;
        set { _cursorPosition = value; OnPropertyChanged(); }
    }
    private string _cursorPosition = "Ln 0, Col 0";

    // Legacy accessors for VCS
    public bool IsGitVisible => Vcs.IsVisible;
    public string BranchName => Vcs.BranchName;
    public int GitChanges => Vcs.ChangedFiles;

    // Legacy accessors for Stats
    public int TotalKeys => Stats.TotalKeys;
    public int TranslatedKeys => Stats.TranslatedKeys;
    public int Errors => Stats.Errors;
    public int Warnings => Stats.Warnings;
    public string TranslationProgress => Stats.Content;

    // Legacy collections — delegate to panel collections
    public System.Collections.ObjectModel.ObservableCollection<string> AvailableLanguages => Language.AvailableLanguages;
    public System.Collections.ObjectModel.ObservableCollection<Core.Models.SummaryItem> LanguageStats => Stats.LanguageStats;
    public System.Collections.ObjectModel.ObservableCollection<string> ChangesSummary => Vcs.ChangesSummary;

    // ═══ Backward-compatible methods ═══

    public void ShowNotification(int count) => Notifications.Update(count);

    public void UpdateSourceControl(string branch, int changes, System.Collections.Generic.IEnumerable<string>? summary = null)
        => Vcs.Update(branch, changes, summary: summary);

    public void UpdateStatistics(int total, int translated, int errors, int warnings, System.Collections.Generic.IEnumerable<Core.Models.SummaryItem>? perLanguage = null)
        => Stats.Update(total, translated, errors, warnings, perLanguage);
}
