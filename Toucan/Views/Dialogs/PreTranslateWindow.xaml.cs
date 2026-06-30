using System.Windows;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs;

public partial class PreTranslateWindow : FluentWindow
{
    public PreTranslateWindow()
    {
        InitializeComponent();
    }

    public PreTranslateWindow(PreTranslateViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreTranslateViewModel vm)
        {
            vm.CancelCommand?.Execute(null);
        }

        DialogResult = false;
        Close();
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreTranslateViewModel vm)
        {
            await vm.StartCommand.ExecuteAsync(null);
        }
        // ponytail: don't close — user needs to see preview results before committing
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreTranslateViewModel vm)
        {
            vm.CommitCommand?.Execute(null);
        }

        DialogResult = true;
        Close();
    }
}
