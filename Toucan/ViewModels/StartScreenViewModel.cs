
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.ViewModels;

public partial class StartScreenViewModel : ObservableObject
{
    private readonly IRecentProjectService _recentProjectService;
    private readonly IProjectService? _projectService;
    private readonly System.Func<Toucan.Core.Models.Project, System.Action<string>, RecentProjectViewModel>? _recentProjectFactory;

    // Observable list of recent projects
    [ObservableProperty]
    private ObservableCollection<RecentProjectViewModel> recentProjects = [];

    // Whether to show "No recent project" state
    public bool HasRecentProjects => RecentProjects?.Count > 0;

    public StartScreenViewModel(IRecentProjectService recentProjectService, IProjectService? projectService = null, System.Func<Toucan.Core.Models.Project, System.Action<string>, RecentProjectViewModel>? recentProjectFactory = null)
    {
        _recentProjectService = recentProjectService;
        _projectService = projectService;
        _recentProjectFactory = recentProjectFactory;

        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        List<Project> recent = _recentProjectService.LoadRecent();
        RecentProjects = new ObservableCollection<RecentProjectViewModel>(
            recent.Select(p => _recentProjectFactory != null ? _recentProjectFactory(p, OpenRecentProject) : new RecentProjectViewModel(p, OpenRecentProject))
        );

        OnPropertyChanged(nameof(HasRecentProjects));
    }

    // 👇 RelayCommands bound to UI buttons
    [RelayCommand]
    private void NewProject()
    {
        // Delegate to MainWindow's NewFolder command via service
        // ponytail: StartScreen will be wired to trigger MainWindowViewModel.NewFolderCommand
    }

    [RelayCommand]
    private void OpenProject()
    {
        // TODO: Show Open File Dialog, then load and open
    }

    [RelayCommand]
    private void Import()
    {
        // TODO: Show import dialog or navigation
    }

    [RelayCommand]
    private void Docs()
    {
        // TODO: Open external documentation site
        _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://docs.toucan.local/",
            UseShellExecute = true
        });
    }

    // Bound from each RecentProject card
    private void OpenRecentProject(string filePath)
    {
        // TODO: Load and switch to main workspace
    }
}
