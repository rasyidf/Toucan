using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Avalonia.ViewModels.StatusBarPanels;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// Modular status bar backed by the panel registry. Typed accessors expose each built-in panel,
/// and the flat properties keep the WPF-era call sites (StatusText, IsLoading, ...) working.
/// </summary>
public partial class StatusBarViewModel : ObservableObject
{
    public StatusBarPanelRegistry Registry { get; } = StatusBarPanelRegistry.Instance;

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

        Mode.Update("Editor");
        Encoding.Update("UTF-8");
        LineEndings.Update(Environment.NewLine == "\r\n" ? "CRLF" : "LF");
        Project.Update(Locales.Loc.T("No project"));
        Notifications.Update(0);
        Loading.Update(false);
    }

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

    [ObservableProperty]
    private string cursorPosition = string.Empty;

    public ObservableCollection<string> AvailableLanguages => Language.AvailableLanguages;

    public void ShowNotification(int count) => NotificationCount = count;

    public void UpdateStatistics(int total, int translated, int errors, int warnings, IEnumerable<SummaryItem>? perLanguage = null)
        => Stats.Update(total, translated, errors, warnings, perLanguage);
}
