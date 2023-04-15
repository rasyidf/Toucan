using OPEdit.Core.Models;
using System.Windows;
using System.Windows.Controls;

namespace OPEdit.Views;

/// <summary>
/// Interaction logic for LanguagesView.xaml
/// </summary>
public partial class LanguagesView : UserControl
{
    private bool isToggle;
    public event RoutedEventHandler NewLanguage;
    public LanguagesView()
    {
        InitializeComponent();
    }

    private void HandleNewLanguage(object sender, RoutedEventArgs e)
    {
        NewLanguage?.Invoke(sender, e);
    }

    private void HandleExpandToggle(object sender, RoutedEventArgs e)
    {

    }
}
