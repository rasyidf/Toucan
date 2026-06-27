using System.Windows;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs;

public partial class ImportProjectDialog : FluentWindow
{
    public ImportProjectViewModel ViewModel { get; }

    public ImportProjectDialog(ImportProjectViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsValid)
            DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
