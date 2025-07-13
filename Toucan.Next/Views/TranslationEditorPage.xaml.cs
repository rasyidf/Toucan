using System.Windows.Controls;

using Toucan.ViewModels;

namespace Toucan.Views;

public partial class TranslationEditorPage : Page
{
    public TranslationEditorPage(TranslationEditorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
