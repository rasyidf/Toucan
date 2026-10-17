using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Components;

namespace Toucan.Avalonia.Views;

/// <summary>Center editor: filter bar, paged list of key cards, and the focused (one-at-a-time) editor.</summary>
public partial class TranslationEditorView : UserControl
{
    private MainWindowViewModel? _viewModel;
    public TranslationEditorView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Classes.Set("narrow", e.NewSize.Width < 560);
        FocusedCard.DataContextChanged += (_, _) => FocusedScroll.Offset = default;
        CardList.AddHandler(KeyDownEvent, OnCardListKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (_viewModel != null) _viewModel.TranslationFocusRequested -= OnTranslationFocusRequested;
            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel != null) _viewModel.TranslationFocusRequested += OnTranslationFocusRequested;
        };
    }

    private void OnCardListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Enter or Key.F2)) return;
        if (e.Source is not (ListBox or ListBoxItem)) return;
        var card = CardList.GetVisualDescendants().OfType<TranslationCard>()
            .FirstOrDefault(c => ReferenceEquals(c.DataContext, CardList.SelectedItem));
        if (card?.FocusEditor() == true) e.Handled = true;
    }

    private void OnTranslationFocusRequested(object? sender, TranslationFocusEventArgs e)
    {
        var item = e.Item;
        if (_viewModel is { FocusedEditorMode: false, SelectedGroup: { } group }) CardList.ScrollIntoView(group);
        Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            var box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.IsEffectivelyVisible
                && b.Classes.Contains("translation") && ReferenceEquals(b.DataContext, item));
            if (box == null) return;
            box.Focus(); box.CaretIndex = box.Text?.Length ?? 0;
        }, DispatcherPriority.Loaded);
    }

    public void FocusFilter()
    {
        FilterBox.Focus();
        FilterBox.SelectAll();
    }
}
