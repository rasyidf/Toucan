using Avalonia.Controls;

namespace Toucan.Avalonia.Views;

/// <summary>Center editor: filter bar, paged list of key cards, and the focused (one-at-a-time) editor.</summary>
public partial class TranslationEditorView : UserControl
{
    public TranslationEditorView() => InitializeComponent();

    public void FocusFilter()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }
}
