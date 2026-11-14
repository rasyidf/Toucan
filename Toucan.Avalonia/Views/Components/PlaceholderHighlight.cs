using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;
using Toucan.Core.Services;

namespace Toucan.Avalonia.Views.Components;

/// <summary>Paints token highlights over the native editor without intercepting text input.</summary>
public sealed class PlaceholderHighlight : Control
{
    public static readonly StyledProperty<TextBox?> EditorProperty =
        AvaloniaProperty.Register<PlaceholderHighlight, TextBox?>(nameof(Editor));
    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<PlaceholderHighlight, IBrush?>(nameof(HighlightBrush));

    private TextBox? _observedEditor;
    private bool _isAttached;

    public TextBox? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }
    public IBrush? HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }

    public PlaceholderHighlight()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        AttachedToVisualTree += (_, _) => { _isAttached = true; ObserveEditor(); };
        DetachedFromVisualTree += (_, _) => { _isAttached = false; StopObserving(); };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty && _isAttached) ObserveEditor();
        InvalidateVisual();
    }

    private void ObserveEditor()
    {
        StopObserving();
        _observedEditor = Editor;
        if (_observedEditor is null) return;
        _observedEditor.PropertyChanged += OnEditorChanged;
        _observedEditor.LayoutUpdated += OnEditorLayoutUpdated;
        InvalidateVisual();
    }

    private void StopObserving()
    {
        if (_observedEditor is null) return;
        _observedEditor.PropertyChanged -= OnEditorChanged;
        _observedEditor.LayoutUpdated -= OnEditorLayoutUpdated;
        _observedEditor = null;
    }

    private void OnEditorChanged(object? sender, AvaloniaPropertyChangedEventArgs e) => InvalidateVisual();
    private void OnEditorLayoutUpdated(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var presenter = Editor?.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        if (presenter is null || !string.IsNullOrEmpty(presenter.PreeditText) || presenter.Text != Editor?.Text || HighlightBrush is null) return;
        var origin = presenter.TranslatePoint(default, this);
        if (origin is null) return;
        var viewport = Editor?.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        var viewportOrigin = viewport?.TranslatePoint(default, this);
        var clip = viewportOrigin is { } point && viewport is not null
            ? new Rect(point, viewport.Bounds.Size) : new Rect(Bounds.Size);
        using (context.PushClip(clip))
        {
            foreach (var token in PlaceholderService.Extract(Editor?.Text ?? string.Empty))
                foreach (var rect in presenter.TextLayout.HitTestTextRange(token.Position, token.Text.Length))
                    context.DrawRectangle(HighlightBrush, null, rect.Translate(new Vector(origin.Value.X, origin.Value.Y)), 2, 2);
        }
    }
}
