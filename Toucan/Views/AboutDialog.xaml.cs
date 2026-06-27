using System;
using System.Windows;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views;

/// <summary>
/// Interaction logic for AboutDialog.xaml
/// </summary>
public partial class AboutDialog : FluentWindow
{
    public AboutDialog(Window parent, Func<Window, AboutViewModel> aboutViewModelFactory)
    {
        Owner = parent;
        ViewModel = aboutViewModelFactory(this);
        DataContext = this;

        InitializeComponent();
    }

    public AboutViewModel ViewModel
    {
        get;
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
