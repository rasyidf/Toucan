using System.Windows;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Toucan.Contracts.Services;
using Toucan.Properties;

namespace Toucan.ViewModels;

// You can show pages in different ways (update main view, navigate, right pane, new windows or dialog)
// using the NavigationService, RightPaneService and WindowManagerService.
// Read more about MenuBar project type here:
// https://github.com/microsoft/TemplateStudio/blob/main/docs/WPF/projectTypes/menubar.md
public class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IWindowManagerService _windowsManagerService;

    private RelayCommand _goBackCommand;
    private ICommand _menuFileSettingsCommand;
    private ICommand _menuViewsProjectCommand;
    private ICommand _menuViewsHomeCommand;
    private ICommand _menuFileExitCommand;
    private ICommand _loadedCommand;
    private ICommand _unloadedCommand;

    public RelayCommand GoBackCommand => _goBackCommand ??= new RelayCommand(OnGoBack, CanGoBack);

    public ICommand MenuFileSettingsCommand => _menuFileSettingsCommand ??= new RelayCommand(OnMenuFileSettings);

    public ICommand MenuFileExitCommand => _menuFileExitCommand ??= new RelayCommand(OnMenuFileExit);

    public ICommand LoadedCommand => _loadedCommand ??= new RelayCommand(OnLoaded);

    public ICommand UnloadedCommand => _unloadedCommand ??= new RelayCommand(OnUnloaded);

    public ShellViewModel(INavigationService navigationService, IWindowManagerService winmanSvc)
    {
        _navigationService = navigationService;
        _windowsManagerService = winmanSvc;
    }

    private void OnLoaded()
    {
        _navigationService.Navigated += OnNavigated;
    }

    private void OnUnloaded()
    { 
        _navigationService.Navigated -= OnNavigated;
    }

    private bool CanGoBack()
        => _navigationService.CanGoBack;

    private void OnGoBack()
        => _navigationService.GoBack();

    private void OnNavigated(object sender, string viewModelName)
    {
        GoBackCommand.NotifyCanExecuteChanged();
    }

    private void OnMenuFileExit()
        => Application.Current.Shutdown();

    public ICommand MenuViewsHomeCommand => _menuViewsHomeCommand ??= new RelayCommand(OnMenuViewsHome);

    private void OnMenuViewsHome()
        => _navigationService.NavigateTo(typeof(HomeViewModel).FullName, null, true);

    public ICommand MenuViewsProjectCommand => _menuViewsProjectCommand ??= new RelayCommand(OnMenuViewsProject);

    private void OnMenuViewsProject()
        => _navigationService.NavigateTo(typeof(ProjectViewModel).FullName, null, true);

    private void OnMenuFileSettings()
        => _navigationService.NavigateTo(typeof(SettingsViewModel).FullName, null, true);
    //=> _windowsManagerService.OpenInDialog(typeof(SettingsViewModel).FullName);
}
