using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toucan.ViewModels;

namespace Toucan.Views;

/// <summary>
/// Interaction logic for TranslationItemView.xaml
/// </summary>
public partial class TranslationItemView : UserControl
{

    public event KeyEventHandler? UpdateLanguageValue;
    public TranslationItemView()
    {
        InitializeComponent();
    }

    private void LanguageValue_KeyUp(object sender, KeyEventArgs e)
    {
        UpdateLanguageValue?.Invoke(sender, e);
    }

    private void ValueTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TranslationItemViewModel tivm }
            && Application.Current.MainWindow?.DataContext is MainWindowViewModel vm)
        {
            vm.FocusedTranslationItem = tivm;
        }
    }
}
