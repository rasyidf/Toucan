using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Editor card for one key. Reports focus and bulk selection to the main view model.</summary>
public partial class TranslationCard : UserControl
{
    public TranslationCard()
    {
        InitializeComponent();
        AddHandler(GotFocusEvent, OnChildGotFocus, RoutingStrategies.Bubble);
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

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not LanguageGroupViewModel group || MainViewModel is not { } vm) return;
        // Leave text boxes their own (cut/copy/paste) context menu.
        if (e.Source is Visual v && v.FindAncestorOfType<TextBox>(includeSelf: true) != null) return;

        var ns = group.Namespace;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Translate Empty Values", Command = group.TranslateKeyCommand });
        menu.Items.Add(new MenuItem { Header = "Copy Key", Command = vm.CopyKeyCommand, CommandParameter = ns });
        var copyAs = new MenuItem { Header = "Copy Key As" };
        for (var i = 0; i < vm.CopyTemplates.Count; i++)
        {
            vm.SelectedGroup = group;
            copyAs.Items.Add(new MenuItem { Header = vm.CopyTemplates[i].Replace("%1", ns, StringComparison.Ordinal), Command = vm.CopyAsTemplateCommand, CommandParameter = i });
        }
        menu.Items.Add(copyAs);
        menu.Items.Add(new Separator());
        if (!group.IsPluralGroup)
        {
            menu.Items.Add(new MenuItem { Header = "Rename…", Command = vm.RenameKeyCommand, CommandParameter = ns });
            menu.Items.Add(new MenuItem { Header = "Duplicate", Command = vm.DuplicateKeyCommand, CommandParameter = ns });
        }
        menu.Items.Add(new MenuItem { Header = "Delete…", Command = vm.DeleteKeyCommand, CommandParameter = ns });
        menu.Open(this);
        e.Handled = true;
    }
}
