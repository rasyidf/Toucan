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
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
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

    internal ContextMenu? LastSpellingMenu { get; private set; }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(includeSelf: true) is { DataContext: TranslationItemViewModel item } box && box.Classes.Contains("translation"))
        {
            var position = box.CaretIndex;
            var presenter = box.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.TextPresenter>().FirstOrDefault();
            if (presenter != null && e.TryGetPosition(presenter, out var pointer)) position = presenter.TextLayout.HitTestPoint(pointer).TextPosition;
            var issue = item.SpellingIssues.FirstOrDefault(i => position >= i.Offset && position <= i.Offset + i.Length);
            if (issue == null || item.IsResourceReadOnly || PanelService.Instance.IsAuditMode) return;
            var entries = new List<MenuItem>();
            foreach (var suggestion in issue.Suggestions)
            {
                var correction = new MenuItem { Header = suggestion };
                correction.Click += (_, _) => { if (item.ApplySpellingCorrection(issue, suggestion)) { box.Focus(); box.CaretIndex = issue.Offset + suggestion.Length; box.SelectionStart = box.SelectionEnd = box.CaretIndex; } };
                entries.Add(correction);
            }
            var ignore = new MenuItem { Header = Locales.Loc.T("Ignore for this session") }; ignore.Click += (_, _) => item.IgnoreSpelling(issue); entries.Add(ignore);
            var user = new MenuItem { Header = Locales.Loc.T("Add to user word list") }; user.Click += (_, _) => item.RememberSpelling(issue, false); entries.Add(user);
            var project = new MenuItem { Header = Locales.Loc.T("Add to project word list") }; project.Click += (_, _) => item.RememberSpelling(issue, true); entries.Add(project);
            // Keep ordinary text actions available alongside corrections.
            var cut = new MenuItem { Header = Locales.Loc.T("Cut") }; cut.Click += (_, _) => box.Cut(); entries.Add(cut);
            var copy = new MenuItem { Header = Locales.Loc.T("Copy") }; copy.Click += (_, _) => box.Copy(); entries.Add(copy);
            var paste = new MenuItem { Header = Locales.Loc.T("Paste") }; paste.Click += (_, _) => box.Paste(); entries.Add(paste);
            var menu = new ContextMenu { ItemsSource = entries }; LastSpellingMenu = menu; menu.Open(box); e.Handled = true; return;
        }
        if (DataContext is not LanguageGroupViewModel group || MainViewModel is not { } vm) return;
        // Leave text boxes their own (cut/copy/paste) context menu.
        if (e.Source is Visual v && v.FindAncestorOfType<TextBox>(includeSelf: true) != null) return;

        KeyMenus.Show(KeyMenus.ForCard(vm, group), this, this, e);
    }
}
