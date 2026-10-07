using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Editor card for one key. Reports focus and bulk selection to the main view model.</summary>
public partial class TranslationCard : UserControl
{
    public TranslationCard()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Classes.Set("narrow", e.NewSize.Width < 480);
        AddHandler(GotFocusEvent, OnChildGotFocus, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnChildKeyDown, RoutingStrategies.Tunnel);
        BulkCheck.IsCheckedChanged += (_, _) =>
        {
            if (DataContext is LanguageGroupViewModel group && MainViewModel is { } vm)
                vm.SetKeySelected(group, BulkCheck.IsChecked == true);
        };
        ContextRequested += OnContextRequested;
    }

    private MainWindowViewModel? MainViewModel => this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel;

    private void OnChildGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: TranslationItemViewModel item } && MainViewModel is { } vm)
            vm.FocusedTranslationItem = item;
    }

    private void OnChildKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Source is TextBox { DataContext: TranslationItemViewModel item } box && MainViewModel is { } vm && vm.AcceptGhostText(item))
        {
            box.CaretIndex = box.Text?.Length ?? 0;
            e.Handled = true;
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not LanguageGroupViewModel group || MainViewModel is not { } vm) return;
        // Leave text boxes their own (cut/copy/paste) context menu.
        if (e.Source is Visual v && v.FindAncestorOfType<TextBox>(includeSelf: true) != null) return;

        KeyMenus.Show(KeyMenus.ForCard(vm, group), this, this, e);
    }
}
