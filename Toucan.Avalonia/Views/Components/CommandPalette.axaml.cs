using Avalonia;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// Quick-open overlay for every menu command (Cmd/Ctrl+Shift+P). Type to filter, Up/Down to move, Enter to run, Esc or a click outside to close.
/// Visibility follows <see cref="MainWindowViewModel.IsCommandPaletteOpen"/>.
/// </summary>
public partial class CommandPalette : UserControl
{
    private const int MaxResults = 80;
    private IReadOnlyList<PaletteCommand> _all = [];
    private readonly ObservableCollection<PaletteCommand> _results = [];

    /// <summary>Supplies the commands each time the palette opens, so enabled state and recent projects are current.</summary>
    public Func<IReadOnlyList<PaletteCommand>>? CommandSource { get; set; }

    /// <summary>Where the search pill sits, in this control's coordinates. The panel opens over it and widens from there.</summary>
    public Func<Rect?>? AnchorSource { get; set; }

    private const double PanelWidth = 580;

    public CommandPalette()
    {
        InitializeComponent();
        List.ItemsSource = _results;
        IsVisible = false;

        Input.TextChanged += (_, _) => Refilter();
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        List.Tapped += (_, e) =>
        {
            if (e.Source is Visual v && v.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: PaletteCommand cmd }) Run(cmd);
        };
        Scrim.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, Scrim)) Close();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty || change.NewValue is not true) return;

        PlaceOverAnchor();
        _all = CommandSource?.Invoke() ?? [];
        Input.Text = string.Empty;
        Refilter();
        Dispatcher.UIThread.Post(() => Input.Focus(), DispatcherPriority.Input);
    }

    private void PlaceOverAnchor()
    {
        if (AnchorSource?.Invoke() is not { } anchor || anchor.Width <= 0 || Bounds.Width <= 0)
        {
            Panel.Margin = new Thickness(0, 52, 0, 0);
            Panel.Width = PanelWidth;
            InputRow.MinHeight = 46;
            return;
        }

        // The input row takes the pill's place, so the pill seems to grow into the palette instead of a second box appearing below it.
        var target = Math.Max(PanelWidth, anchor.Width);
        var shift = anchor.Center.X - Bounds.Width / 2; // the panel is centered; Margin moves its center
        Panel.Margin = new Thickness(shift > 0 ? 2 * shift : 0, Math.Max(0, anchor.Top), shift < 0 ? -2 * shift : 0, 0);
        InputRow.MinHeight = anchor.Height;
        Panel.Width = anchor.Width;
        Dispatcher.UIThread.Post(() => Panel.Width = target, DispatcherPriority.Render);
    }

    private void Close()
    {
        if (DataContext is MainWindowViewModel vm) vm.IsCommandPaletteOpen = false;
    }

    private void Run(PaletteCommand command)
    {
        if (!command.CanRun) return;
        Close();
        // Run after the overlay is gone so dialogs and focus changes start from the normal window.
        Dispatcher.UIThread.Post(() => command.Command.Execute(command.Parameter), DispatcherPriority.Background);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Down: Move(+1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter:
                if (List.SelectedItem is PaletteCommand cmd) Run(cmd);
                e.Handled = true;
                break;
        }
    }

    private void Move(int delta)
    {
        if (_results.Count == 0) return;
        var next = Math.Clamp(List.SelectedIndex + delta, 0, _results.Count - 1);
        List.SelectedIndex = next;
        List.ScrollIntoView(_results[next]);
    }

    private void Refilter()
    {
        _results.Clear();
        foreach (var c in Rank(_all, Input.Text ?? string.Empty).Take(MaxResults)) _results.Add(c);
        Empty.IsVisible = _results.Count == 0;
        List.IsVisible = _results.Count > 0;
        if (_results.Count > 0) List.SelectedIndex = 0;
    }

    /// <summary>
    /// Every typed word must appear in "category title". Title-prefix matches rank first, then word-start matches, then anywhere;
    /// commands that cannot run right now sink to the bottom. With no text, the menu order is kept.
    /// </summary>
    internal static IEnumerable<PaletteCommand> Rank(IReadOnlyList<PaletteCommand> all, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return all.OrderBy(c => c.CanRun ? 0 : 1);

        return all
            .Select((c, index) => (Command: c, Index: index, Score: Score(c, terms)))
            .Where(x => x.Score >= 0)
            .OrderBy(x => x.Score + (x.Command.CanRun ? 0 : 100))
            .ThenBy(x => x.Index)
            .Select(x => x.Command);
    }

    private static int Score(PaletteCommand command, string[] terms)
    {
        var title = command.Title;
        var haystack = $"{command.Category} {title}";
        var score = 0;
        foreach (var term in terms)
        {
            if (!haystack.Contains(term, StringComparison.CurrentCultureIgnoreCase)) return -1;
            if (title.StartsWith(term, StringComparison.CurrentCultureIgnoreCase)) continue;
            var at = title.IndexOf(term, StringComparison.CurrentCultureIgnoreCase);
            score += at > 0 && !char.IsLetterOrDigit(title[at - 1]) ? 1 : at >= 0 ? 2 : 3;
        }
        return score;
    }
}
