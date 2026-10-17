using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.ComponentModel;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Editor card for one key. Reports focus and bulk selection to the main view model.</summary>
public partial class TranslationCard : UserControl
{
    public static readonly DirectProperty<TranslationCard, int> ComparisonColumnsProperty =
        AvaloniaProperty.RegisterDirect<TranslationCard, int>(nameof(ComparisonColumns), c => c.ComparisonColumns);
    private int _comparisonColumns = 1;
    public int ComparisonColumns => _comparisonColumns;
    private readonly List<TranslationItemViewModel> _layoutItems = [];

    public TranslationCard()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            var columns = e.NewSize.Width >= 680 ? 2 : 1;
            SetAndRaise(ComparisonColumnsProperty, ref _comparisonColumns, columns);
            Classes.Set("comparison", columns == 2);
            Classes.Set("narrow", e.NewSize.Width < 480 || columns == 2);
            UpdateColumnLayout();
        };
        DataContextChanged += (_, _) => ObserveLayoutItems();
        AttachedToVisualTree += (_, _) => ObserveLayoutItems();
        DetachedFromVisualTree += (_, _) => ClearLayoutItems();
        AddHandler(GotFocusEvent, OnChildGotFocus, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnChildKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnRowPointerPressed, RoutingStrategies.Bubble);
        BulkCheck.IsCheckedChanged += (_, _) =>
        {
            if (DataContext is LanguageGroupViewModel group && MainViewModel is { } vm)
                vm.SetKeySelected(group, BulkCheck.IsChecked == true);
        };
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    private MainWindowViewModel? MainViewModel => this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel;

    private void ObserveLayoutItems()
    {
        ClearLayoutItems();
        if (DataContext is LanguageGroupViewModel group)
        {
            _layoutItems.AddRange(group.AllItems);
            foreach (var item in _layoutItems) item.PropertyChanged += OnLayoutItemChanged;
        }
        UpdateColumnLayout();
    }

    private void ClearLayoutItems()
    {
        foreach (var item in _layoutItems) item.PropertyChanged -= OnLayoutItemChanged;
        _layoutItems.Clear();
    }

    private void OnLayoutItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TranslationItemViewModel.IsLanguageVisible)) UpdateColumnLayout();
    }

    private void UpdateColumnLayout()
    {
        var languages = _layoutItems.Where(i => i.IsLanguageVisible).Select(i => i.Language).Distinct(StringComparer.Ordinal).Count();
        Classes.Set("columns", Bounds.Width >= Math.Max(480, 180 + languages * 190));
    }

    private void OnEditClick(object? sender, RoutedEventArgs e) => FocusEditor();

    public bool FocusEditor()
    {
        var boxes = this.GetVisualDescendants().OfType<TextBox>()
            .Where(b => b.Classes.Contains("translation") && b.IsEffectivelyVisible && !b.IsReadOnly).ToList();
        var language = MainViewModel?.FocusedTranslationItem?.Language;
        var box = boxes.FirstOrDefault(b => b.DataContext is TranslationItemViewModel item && item.Language == language)
            ?? boxes.FirstOrDefault(b => b.DataContext is TranslationItemViewModel { HasSource: true })
            ?? boxes.FirstOrDefault();
        if (box is null) return false;
        box.Focus();
        box.CaretIndex = box.Text?.Length ?? 0;
        box.BringIntoView();
        return true;
    }

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source) return;
        // Text selection and action buttons retain their own pointer behavior.
        if (source.FindAncestorOfType<TextBox>(includeSelf: true) != null ||
            source.FindAncestorOfType<Button>(includeSelf: true) != null) return;
        var row = source is Grid grid && grid.Classes.Contains("translationRow") ? grid
            : source.GetVisualAncestors().OfType<Grid>().FirstOrDefault(g => g.Classes.Contains("translationRow"));
        var box = row?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Classes.Contains("translation"));
        if (box is null || box.IsReadOnly) return;
        box.Focus();
        box.CaretIndex = box.Text?.Length ?? 0;
        box.BringIntoView();
        e.Handled = true;
    }

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
