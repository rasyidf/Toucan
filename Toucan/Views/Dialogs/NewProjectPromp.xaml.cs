using System.Windows.Input;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan;

/// <summary>
/// Interaction logic for PromptDialog.xaml
/// </summary>
public partial class NewProjectPrompt : FluentWindow
{
    public NewProjectPrompt(string title, string message, NewProjectViewModel vm, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;

        if (!string.IsNullOrEmpty(defaultValue))
        {
            vm.ProjectName = defaultValue;
        }

        DataContext = vm;
        // give keyboard focus to the project name textbox when available
        _ = (ProjectNameTextBox?.Focus());
        ProjectNameTextBox?.SelectAll();

        RoutedCommand saveCommand = new();
        _ = saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        _ = CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        _ = refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        _ = CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));
    }

    // DI constructor: resolves a fully configured NewProjectViewModel from DI
    public NewProjectPrompt(NewProjectViewModel vm, string defaultValue = "")
    {
        InitializeComponent();
        Title = "New Project";
        if (!string.IsNullOrEmpty(defaultValue))
        {
            vm.ProjectName = defaultValue;
        }

        DataContext = vm;
        _ = (ProjectNameTextBox?.Focus());
        ProjectNameTextBox?.SelectAll();

        RoutedCommand saveCommand = new();
        _ = saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        _ = CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        _ = refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        _ = CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));
    }

    public string ResponseText
    {
        get => (DataContext as NewProjectViewModel)?.ProjectName ?? string.Empty;
        set
        {
            if (DataContext is NewProjectViewModel vm)
            {
                vm.ProjectName = value;
            }
        }
    }


    private void CancelDialog(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void FrameworkTile_Checked(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is FrameworkTile tile)
        {
            if (DataContext is NewProjectViewModel vm)
            {
                vm.SelectedFramework = tile;
            }
        }
    }

    private void OKButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not NewProjectViewModel vm)
        {
            DialogResult = true;
            return;
        }

        if (!vm.IsValid)
        {
            // a basic validation alert
            _ = System.Windows.MessageBox.Show("Please set a project name and folder.", "Invalid", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
            _ = System.Windows.MessageBox.Show($"Failed to create project: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
