using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using System;
using Wpf.Ui.Controls;

namespace Toucan.Views;

/// <summary>
/// Interaction logic for AboutDialog.xaml
/// </summary>
public partial class AboutDialog : FluentWindow
{

    public AboutDialog(Window parent)
    {
        Owner = parent;
        // Try to get a DI-created instance (factory) first, fall back to direct construction
        var factory = App.Services?.GetService(typeof(Func<Window, ViewModels.AboutViewModel>)) as Func<Window, ViewModels.AboutViewModel>;
        ViewModel = factory != null ? factory(this) : new ViewModels.AboutViewModel(this);
        DataContext = this;

        InitializeComponent();
    }
    public ViewModels.AboutViewModel ViewModel
    {
        get;
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
