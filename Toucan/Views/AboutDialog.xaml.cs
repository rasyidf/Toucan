using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Wpf.Ui.Controls.Window;

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
