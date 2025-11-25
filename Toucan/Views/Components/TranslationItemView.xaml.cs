using System.Windows.Controls;
using System.Windows.Input;

namespace Toucan.Views;

/// <summary>
/// Interaction logic for TranslationItemView.xaml
/// </summary>
public partial class TranslationItemView : UserControl
{

    public event KeyEventHandler UpdateLanguageValue;
    public TranslationItemView()
    {
        InitializeComponent();
    }

    private void LanguageValue_KeyUp(object sender, KeyEventArgs e)
    {
        UpdateLanguageValue?.Invoke(sender, e);
    }
}
