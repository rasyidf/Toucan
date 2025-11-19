using System.Windows.Input;
using Wpf.Ui.Controls;
using System.Linq;

namespace Toucan;

/// <summary>
/// Interaction logic for PromptDialog.xaml
/// </summary>
partial class NewProjectPrompt : FluentWindow
{



    public NewProjectPrompt(string title, string message, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
         
        ResponseTextBox.Text = defaultValue;
        ResponseTextBox.Focus();
        ResponseTextBox.SelectAll();

        // Setup view model
        var vm = new ViewModels.NewProjectViewModel();
        ResponseTextBox.SetBinding(System.Windows.Controls.TextBox.TextProperty, new System.Windows.Data.Binding("ProjectName") { Source = vm, Mode = System.Windows.Data.BindingMode.TwoWay });
        DataContext = vm;

        RoutedCommand saveCommand = new();
        saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));



    }

    public string ResponseText
    {
        get { return ResponseTextBox.Text; }
        set { ResponseTextBox.Text = value; }
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

        if (!vm.IsValid())
        {
            // a basic validation alert
            System.Windows.MessageBox.Show("Please set a project name, folder, and a translation path for each language in every package.", "Invalid", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void AddLanguage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.NewProjectViewModel vm)
        {
            vm.AddLanguageCommand.Execute(null);
            // Additional UI updates may be needed
        }
    }

    private void RemoveLanguage_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.NewProjectViewModel vm)
        {
            if (vm.Languages.Count > 0)
            {
                // Remove last for simplicity
                vm.RemoveLanguageCommand.Execute(vm.Languages.Last());
            }
        }
    }

    private void BrowseTranslation_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // Figure out which package and language this button belongs to
        if (sender is System.Windows.Controls.Button b)
        {
            // Visual tree traversal to get DataContext of the translation entry and the parent package
            var entry = b.DataContext as TranslationEntry;
            if (entry == null) return;

            // find parent package DataContext
            var container = System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(System.Windows.Media.VisualTreeHelper.GetParent(b) as System.Windows.DependencyObject);

            // Fallback: call viewmodel BrowseTranslationFileCommand by looking up package index
            if (DataContext is ViewModels.NewProjectViewModel vm)
            {
                // find which package contains this entry
                int packageIndex = -1;
                for (int i = 0; i < vm.Packages.Count; i++)
                {
                    if (vm.Packages[i].Translations.Contains(entry))
                    {
                        packageIndex = i;
                        break;
                    }
                }

                if (packageIndex >= 0)
                {
                    vm.BrowseTranslationFileCommand.Execute($"{packageIndex}|{entry.Language}");
                }
            }
        }
    }
}
