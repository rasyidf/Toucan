using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public partial class StartScreenViewModel : ObservableObject
{
    private readonly IRecentProjectService _recentProjectService;

    [ObservableProperty] private ObservableCollection<RecentProjectViewModel> recentProjects = [];
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
            recent.Select(p => new RecentProjectViewModel(p, OpenRecentProject)));
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    [RelayCommand] private void NewProject() { /* wired to MainWindowViewModel.NewFolder */ }
    [RelayCommand] private void OpenProject() { /* wired to MainWindowViewModel.OpenFolder */ }
    [RelayCommand] private void Import() { }
    [RelayCommand] private void Docs() => Process.Start(new ProcessStartInfo("https://toucan.rasyid.dev") { UseShellExecute = true });

    private void OpenRecentProject(string filePath) { /* TODO: wire to MainWindowViewModel.LoadFolder */ }
}

public partial class RecentProjectViewModel : ObservableObject
{
    public string FilePath { get; }
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(FilePath);
    public RelayCommand OpenProjectCommand { get; }

    public RecentProjectViewModel(Project project, Action<string> openAction)
    {
        FilePath = project.Path;
        OpenProjectCommand = new RelayCommand(() => openAction?.Invoke(FilePath));
    }
}
