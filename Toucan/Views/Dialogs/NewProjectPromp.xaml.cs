using System.Windows.Input;
using Wpf.Ui.Controls;
using Toucan.Core.Contracts.Services;

namespace Toucan;

/// <summary>
/// Interaction logic for PromptDialog.xaml
/// </summary>
partial class NewProjectPrompt : FluentWindow
{
    private readonly IProjectService _projectService;

    public NewProjectPrompt(string title, string message, IProjectService projectService, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        _projectService = projectService;
         
        // if a default value was provided, push it into the view model

        // Setup view model with project service
        var vm = new ViewModels.NewProjectViewModel(projectService);
        if (!string.IsNullOrEmpty(defaultValue)) vm.ProjectName = defaultValue;
        DataContext = vm;
        // give keyboard focus to the project name textbox when available
        ProjectNameTextBox?.Focus();
        ProjectNameTextBox?.SelectAll();

        RoutedCommand saveCommand = new();
        saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));



    }

    // DI constructor: resolves a fully configured NewProjectViewModel from DI
    public NewProjectPrompt(ViewModels.NewProjectViewModel vm, string defaultValue = "")
    {
        InitializeComponent();
        Title = "New Project";
        if (!string.IsNullOrEmpty(defaultValue)) vm.ProjectName = defaultValue;
        DataContext = vm;
        ProjectNameTextBox?.Focus();
        ProjectNameTextBox?.SelectAll();

        RoutedCommand saveCommand = new();
        saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));
    }

    public string ResponseText
    {
        get { return (DataContext as ViewModels.NewProjectViewModel)?.ProjectName ?? string.Empty; }
        set { if (DataContext is ViewModels.NewProjectViewModel vm) vm.ProjectName = value; }
    }


    private void CancelDialog(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OKButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var vm = DataContext as ViewModels.NewProjectViewModel;
        if (vm == null)
        {
            DialogResult = true;
            return;
        }

        if (!vm.IsValid)
        {
            // a basic validation alert
            System.Windows.MessageBox.Show("Please set a project name and folder.", "Invalid", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Create the actual project files
            vm.CreateProject();
            DialogResult = true;
        }
        catch (System.Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to create project: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
