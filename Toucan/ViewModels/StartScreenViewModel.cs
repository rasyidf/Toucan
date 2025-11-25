
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;

namespace Toucan.ViewModels;

public partial class StartScreenViewModel : ObservableObject
{
    private readonly IRecentProjectService _recentProjectService;
    private readonly IProjectService _projectService;

    // Observable list of recent projects
    [ObservableProperty]
    private ObservableCollection<RecentProjectViewModel> recentProjects = [];

    // Whether to show "No recent project" state
    public bool HasRecentProjects => RecentProjects?.Count > 0;

    public StartScreenViewModel(IRecentProjectService recentProjectService, IProjectService projectService = null)
    {
        _recentProjectService = recentProjectService;
        _projectService = projectService;

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
        // Show the New Project dialog
        NewProjectPrompt dialog;
        if (App.Services != null)
            dialog = App.Services.GetService(typeof(NewProjectPrompt)) as NewProjectPrompt ?? new NewProjectPrompt("New Project", "Create a new translation project", _projectService);
        else
            dialog = new NewProjectPrompt("New Project", "Create a new translation project", _projectService);
        dialog.Owner = System.Windows.Application.Current.MainWindow;

        if (dialog.ShowDialog() == true && dialog.DataContext is NewProjectViewModel vm)
        {
            // Add to recent projects if a folder is set
            if (!string.IsNullOrWhiteSpace(vm.ProjectFolder))
            {
                _recentProjectService.Add(vm.ProjectFolder);
            }
        }
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
