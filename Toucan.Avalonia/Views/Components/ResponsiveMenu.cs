using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// A menu bar that folds the items that do not fit into a trailing "…" menu, in order, so a narrow window never overlaps
/// the menu with the controls beside it. Constrain its width (for example with the host's <c>MaxWidth</c>) and it fits itself.
/// </summary>
public sealed class ResponsiveMenu : Menu
{
    private const double OverflowWidth = 40;

    private readonly List<MenuItem> _all = [];
    private readonly MenuItem _overflow = new() { Header = "…" };
    private double[]? _widths;
    private int _shown = -1;
    private bool _applyQueued;

    /// <summary>Reuse the stock Menu template and styles.</summary>
    protected override Type StyleKeyOverride => typeof(Menu);

    public ResponsiveMenu() => _overflow.Classes.Add("overflow");

    /// <summary>Number of top-level items currently shown directly in the bar (the rest are in the overflow menu).</summary>
    public int ShownCount => _shown;

    public void SetItems(IEnumerable<MenuItem> items)
    {
        _all.Clear();
        _all.AddRange(items);
        _widths = null;
        Apply(_all.Count);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_widths == null && _shown == _all.Count && _all.Count > 0)
        {
            // Everything is in the bar: measure each item at its natural width once.
            base.MeasureOverride(new Size(double.PositiveInfinity, availableSize.Height));
            var widths = new double[_all.Count];
            for (var i = 0; i < _all.Count; i++)
            {
                widths[i] = _all[i].DesiredSize.Width;
                if (widths[i] <= 0) { widths = null!; break; }
            }
            _widths = widths;
        }

        if (_widths != null)
        {
            var fit = Fit(_widths, availableSize.Width);
            if (fit != _shown) QueueApply(fit);
        }
        return base.MeasureOverride(availableSize);
    }

    /// <summary>How many of the items fit in <paramref name="available"/> width, leaving room for the overflow button when some do not.</summary>
    public static int Fit(IReadOnlyList<double> widths, double available)
    {
        if (double.IsInfinity(available) || widths.Sum() <= available) return widths.Count;
        var used = OverflowWidth;
        var count = 0;
        foreach (var w in widths)
        {
            if (used + w > available) break;
            used += w;
            count++;
        }
        return count;
    }

    private void QueueApply(int shown)
    {
        if (_applyQueued) return;
        _applyQueued = true;
        // Changing the items inside a measure pass would invalidate the layout that is running.
        Dispatcher.UIThread.Post(() => { _applyQueued = false; Apply(shown); InvalidateMeasure(); }, DispatcherPriority.Render);
    }

    private void Apply(int shown)
    {
        shown = Math.Clamp(shown, 0, _all.Count);
        if (shown == _shown) return;
        _shown = shown;

        Items.Clear();
        _overflow.Items.Clear();
        foreach (var item in _all.Take(shown)) Items.Add(item);
        if (shown < _all.Count)
        {
            foreach (var item in _all.Skip(shown)) _overflow.Items.Add(item);
            Items.Add(_overflow);
        }
    }
}
