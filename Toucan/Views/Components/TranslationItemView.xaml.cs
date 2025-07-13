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
using System.Windows.Navigation;
using System.Windows.Shapes;

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
