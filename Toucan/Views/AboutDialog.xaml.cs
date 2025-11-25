using System.Windows;
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
        ViewModel = new ViewModels.AboutViewModel(this);
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
