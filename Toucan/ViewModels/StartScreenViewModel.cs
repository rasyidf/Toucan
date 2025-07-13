﻿ 
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Toucan.Core.Contracts;
using Toucan.Services;

namespace Toucan.ViewModels;

public partial class StartScreenViewModel : ObservableObject
{
    private readonly IRecentProjectService _recentProjectService;

    // Observable list of recent projects
    [ObservableProperty]
    private ObservableCollection<RecentProjectViewModel> recentProjects = [];

    // Whether to show "No recent project" state
    public bool HasRecentProjects => RecentProjects?.Count > 0;

    public StartScreenViewModel(IRecentProjectService recentProjectService)
    {
        _recentProjectService = recentProjectService;

        LoadRecentProjects();
    }

    private void LoadRecentProjects()
    {
        var recent = _recentProjectService.LoadRecent();
        RecentProjects = new ObservableCollection<RecentProjectViewModel>(
            recent.Select(p => new RecentProjectViewModel(p, OpenRecentProject))
        );

        OnPropertyChanged(nameof(HasRecentProjects));
    }

    // 👇 RelayCommands bound to UI buttons
    [RelayCommand]
    private void NewProject()
    {
        // TODO: Navigate to New Project screen or show dialog
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
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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
