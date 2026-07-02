using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Toucan.Core.Models;
using Toucan.ViewModels;

namespace Toucan.Views;

/// <summary>
/// Interaction logic for TranslationDetailsView.xaml
/// </summary>
public partial class TranslationDetailsView : UserControl
{
    public event RoutedEventHandler? FirstPageClick;
    public event RoutedEventHandler? LastPageClick;
    public event RoutedEventHandler? PreviousPageClick;
    public event RoutedEventHandler? NextPageClick;
    public event RoutedEventHandler? ShowAllClick;
    public event RoutedEventHandler? UpdateLanguageValue;

    // ponytail: debounce UpdateLanguageValue so summary recalc doesn't fire per-keystroke
    private readonly DispatcherTimer _updateDebounce;

    public TranslationDetailsView()
    {
        InitializeComponent();
        _updateDebounce = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(300) };
        _updateDebounce.Tick += (_, _) => { _updateDebounce.Stop(); UpdateLanguageValue?.Invoke(this, new RoutedEventArgs()); };
    }

    private void FirstPage(object sender, RoutedEventArgs e)
    {
        FirstPageClick?.Invoke(this, e);
    }

    private void PreviousPage(object sender, RoutedEventArgs e)
    {
        PreviousPageClick?.Invoke(this, e);
    }

    private void NextPage(object sender, RoutedEventArgs e)
    {
        NextPageClick?.Invoke(this, e);

    }

    private void LastPage(object sender, RoutedEventArgs e)
    {
        LastPageClick?.Invoke(sender, e);
    }

    private void ShowAll(object sender, RoutedEventArgs e)
    {
        ShowAllClick?.Invoke(sender, e);
    }

    private void LanguageValue_KeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox txtBox)
        {
            return;
        }

        // Tag can be either the model (TranslationItem) or a TranslationItemViewModel
        if (txtBox.Tag is TranslationItem model)
        {
            model.Value = txtBox.Text;
        }
        else if (txtBox.Tag is TranslationItemViewModel vm)
        {
            // The Text binding already updates VM.Value, but set it explicitly to be safe
            vm.Value = txtBox.Text;
        }

        // Debounce: restart timer on each keystroke, fires once after 300ms idle
        _updateDebounce.Stop();
        _updateDebounce.Start();
    }
}
