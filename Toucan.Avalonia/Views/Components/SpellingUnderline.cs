using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Toucan.Avalonia.ViewModels;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Draws against the actual text layout, including wrapped and right-to-left runs.</summary>
public sealed class SpellingUnderline : Control
{
    private TranslationItemViewModel? _item;
    private bool _attached;
    private static readonly Pen Underline = new(Brushes.IndianRed, 1.2, new DashStyle([2, 2], 0));
    public SpellingUnderline()
    {
        IsHitTestVisible = false; ClipToBounds = true;
        AttachedToVisualTree += (_, _) => { _attached = true; Connect(); };
        DetachedFromVisualTree += (_, _) => { _attached = false; Disconnect(); };
        DataContextChanged += (_, _) => { Disconnect(); if (_attached) Connect(); };
        LayoutUpdated += (_, _) => InvalidateVisual();
    }
    private void Connect()
    {
        if (DataContext is not TranslationItemViewModel item || ReferenceEquals(_item, item)) return;
        _item = item; _item.PropertyChanged += Changed; _item.SpellingActive = true;
    }
    private void Disconnect()
    { if (_item != null) { _item.PropertyChanged -= Changed; _item.SpellingActive = false; _item = null; } }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(TranslationItemViewModel.SpellingIssues) or nameof(TranslationItemViewModel.Value)) InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_item == null || _item.CheckedSpellingText != _item.Value || Parent is not Panel panel) return;
        var presenter = panel.Children.OfType<TextBox>().FirstOrDefault()?.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        if (presenter == null || presenter.TranslatePoint(default, this) is not { } origin) return;
        using (context.PushClip(new Rect(Bounds.Size)))
        {
            double y = 0;
            foreach (var line in presenter.TextLayout.TextLines)
            {
                foreach (var issue in _item.SpellingIssues)
                {
                    var start = Math.Max(line.FirstTextSourceIndex, issue.Offset);
                    var end = Math.Min(line.FirstTextSourceIndex + line.Length, issue.Offset + issue.Length);
                    if (start >= end) continue;
                    foreach (var bounds in line.GetTextBounds(start, end - start))
                    {
                        var rectangle = bounds.Rectangle;
                        var bottom = origin.Y + y + rectangle.Bottom - 1;
                        context.DrawLine(Underline, new Point(origin.X + rectangle.Left, bottom), new Point(origin.X + rectangle.Right, bottom));
                    }
                }
                y += line.Height;
            }
        }
    }
}
