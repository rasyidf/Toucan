using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Toucan.Avalonia.Locales;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// A settings card that manages a list of <see cref="SettingsRow"/> items: header with item count, a filter box (shown once the
/// list is long enough), a scroll area capped at <see cref="MaxListHeight"/>, an empty state and an "add" row.
/// Use it for every editable or browsable list on a settings page so they all look and behave the same.
/// </summary>
public class SettingsList : ItemsControl, ISettingsSection
{
    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<SettingsList, string?>(nameof(Header));
    public static readonly StyledProperty<string?> FooterProperty = AvaloniaProperty.Register<SettingsList, string?>(nameof(Footer));
    public static readonly StyledProperty<string?> EmptyTextProperty = AvaloniaProperty.Register<SettingsList, string?>(nameof(EmptyText));
    public static readonly StyledProperty<string?> AddTextProperty = AvaloniaProperty.Register<SettingsList, string?>(nameof(AddText));
    public static readonly StyledProperty<ICommand?> AddCommandProperty = AvaloniaProperty.Register<SettingsList, ICommand?>(nameof(AddCommand));
    public static readonly StyledProperty<bool> CanAddProperty = AvaloniaProperty.Register<SettingsList, bool>(nameof(CanAdd), true);
    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<SettingsList, object?>(nameof(Actions));
    public static readonly StyledProperty<object?> ColumnHeaderProperty = AvaloniaProperty.Register<SettingsList, object?>(nameof(ColumnHeader));
    public static readonly StyledProperty<double> MaxListHeightProperty = AvaloniaProperty.Register<SettingsList, double>(nameof(MaxListHeight), 320);
    public static readonly StyledProperty<bool?> ShowSearchProperty = AvaloniaProperty.Register<SettingsList, bool?>(nameof(ShowSearch));
    public static readonly StyledProperty<int> SearchThresholdProperty = AvaloniaProperty.Register<SettingsList, int>(nameof(SearchThreshold), 8);
    public static readonly StyledProperty<string> SearchTextProperty = AvaloniaProperty.Register<SettingsList, string>(nameof(SearchText), string.Empty);
    public static readonly StyledProperty<bool> IsMatchProperty = AvaloniaProperty.Register<SettingsList, bool>(nameof(IsMatch), true);

    public static readonly StyledProperty<bool> IsSearchBarVisibleProperty = AvaloniaProperty.Register<SettingsList, bool>(nameof(IsSearchBarVisible));
    public static readonly StyledProperty<string?> CountTextProperty = AvaloniaProperty.Register<SettingsList, string?>(nameof(CountText));
    public static readonly StyledProperty<string?> EmptyMessageProperty = AvaloniaProperty.Register<SettingsList, string?>(nameof(EmptyMessage));

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    /// <summary>Explanatory text under the card; hidden together with the list.</summary>
    public string? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    /// <summary>Shown inside the card while the list has no items.</summary>
    public string? EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    /// <summary>Label of the link button under the rows. The button only appears when <see cref="AddCommand"/> is set.</summary>
    public string? AddText { get => GetValue(AddTextProperty); set => SetValue(AddTextProperty, value); }
    public ICommand? AddCommand { get => GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public bool CanAdd { get => GetValue(CanAddProperty); set => SetValue(CanAddProperty, value); }
    /// <summary>Extra controls (e.g. a "Clear" button) shown at the right of the header line.</summary>
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    /// <summary>A header line above the rows (column titles of a table). Shown only while the list has items.</summary>
    public object? ColumnHeader { get => GetValue(ColumnHeaderProperty); set => SetValue(ColumnHeaderProperty, value); }
    /// <summary>The rows scroll once they are taller than this. Use <see cref="double.PositiveInfinity"/> for no limit.</summary>
    public double MaxListHeight { get => GetValue(MaxListHeightProperty); set => SetValue(MaxListHeightProperty, value); }
    /// <summary>True or false forces the filter box on or off; null (default) shows it from <see cref="SearchThreshold"/> items.</summary>
    public bool? ShowSearch { get => GetValue(ShowSearchProperty); set => SetValue(ShowSearchProperty, value); }
    public int SearchThreshold { get => GetValue(SearchThresholdProperty); set => SetValue(SearchThresholdProperty, value); }
    public string SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public bool IsMatch { get => GetValue(IsMatchProperty); set => SetValue(IsMatchProperty, value); }

    public bool IsSearchBarVisible { get => GetValue(IsSearchBarVisibleProperty); private set => SetValue(IsSearchBarVisibleProperty, value); }
    /// <summary>"12" or, while filtering, "3 of 12".</summary>
    public string? CountText { get => GetValue(CountTextProperty); private set => SetValue(CountTextProperty, value); }
    /// <summary>Empty-state or "no matches" message; null while there are rows to show.</summary>
    public string? EmptyMessage { get => GetValue(EmptyMessageProperty); private set => SetValue(EmptyMessageProperty, value); }

    private bool _refreshQueued;

    public SettingsList()
    {
        ContainerPrepared += (_, _) => QueueRefresh();
        ContainerClearing += (_, _) => QueueRefresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SearchTextProperty || change.Property == ItemCountProperty || change.Property == ShowSearchProperty
            || change.Property == SearchThresholdProperty || change.Property == EmptyTextProperty)
            QueueRefresh();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        QueueRefresh();
    }

    /// <summary>Coalesces bursts (a bound collection filling up creates one container per item).</summary>
    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() => { _refreshQueued = false; Refresh(); }, DispatcherPriority.Loaded);
    }

    private void Refresh()
    {
        var terms = SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var total = ItemCount;
        var visible = 0;
        var first = true;

        for (var i = 0; i < total; i++)
        {
            if (ContainerFromIndex(i) is not Control container) continue;
            var show = terms.Length == 0 || MatchesAll(container, terms);
            container.IsVisible = show;
            container.Classes.Set("firstVisible", show && first);
            if (!show) continue;
            visible++;
            first = false;
        }

        IsSearchBarVisible = ShowSearch ?? total >= SearchThreshold;
        CountText = total == 0 ? null : terms.Length == 0 ? total.ToString() : Loc.T("{0} of {1}").Replace("{0}", visible.ToString()).Replace("{1}", total.ToString());
        EmptyMessage = total == 0 ? EmptyText : visible == 0 && terms.Length > 0 ? Loc.T("No matches") : null;
    }

    /// <summary>A row matches when every word appears in any text it displays or edits.</summary>
    private static bool MatchesAll(Control container, string[] terms)
    {
        var text = string.Join(' ', container.GetVisualDescendants().Select(v => v switch
        {
            SettingsRow r => $"{r.Title} {r.Description}",
            TextBox t => t.Text,
            TextBlock t => t.Text,
            _ => null,
        }).Where(s => !string.IsNullOrEmpty(s)));
        return terms.All(t => text.Contains(t, StringComparison.CurrentCultureIgnoreCase));
    }
}
