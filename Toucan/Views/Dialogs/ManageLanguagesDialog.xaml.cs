using System.Windows;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs;

/// <summary>
/// Manage Languages dialog — uses the shared LanguageListEditor component.
/// </summary>
public partial class ManageLanguagesDialog : FluentWindow
{
    public LanguageManagerViewModel ViewModel { get; }

    public ManageLanguagesDialog(LanguageManagerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void LanguageEditor_LanguageAdded(object? sender, Components.LanguageEntryEventArgs e)
    {
        ViewModel.AddedLanguages.Add(e.Entry.Code);
    }

    private void LanguageEditor_LanguageRemoved(object? sender, Components.LanguageEntryEventArgs e)
    {
        ViewModel.RemovedLanguages.Add(e.Entry.Code);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
