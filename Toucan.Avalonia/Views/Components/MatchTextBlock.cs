using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Toucan.Avalonia.Views.Components;

/// <summary>
/// A line of search-result text with the match highlighted. With <see cref="ReplacementText"/> set it shows the change inline:
/// the matched text struck through, followed by what it would become. Long text is trimmed to a window around the match.
/// </summary>
public class MatchTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> SourceTextProperty = AvaloniaProperty.Register<MatchTextBlock, string?>(nameof(SourceText));
    public static readonly StyledProperty<int> MatchStartProperty = AvaloniaProperty.Register<MatchTextBlock, int>(nameof(MatchStart));
    public static readonly StyledProperty<int> MatchLengthProperty = AvaloniaProperty.Register<MatchTextBlock, int>(nameof(MatchLength));
    public static readonly StyledProperty<string?> ReplacementTextProperty = AvaloniaProperty.Register<MatchTextBlock, string?>(nameof(ReplacementText));

    /// <summary>Characters kept before the match when the text is trimmed.</summary>
    private const int LeadingContext = 24;

    public string? SourceText { get => GetValue(SourceTextProperty); set => SetValue(SourceTextProperty, value); }
    public int MatchStart { get => GetValue(MatchStartProperty); set => SetValue(MatchStartProperty, value); }
    public int MatchLength { get => GetValue(MatchLengthProperty); set => SetValue(MatchLengthProperty, value); }
    public string? ReplacementText { get => GetValue(ReplacementTextProperty); set => SetValue(ReplacementTextProperty, value); }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceTextProperty || change.Property == MatchStartProperty || change.Property == MatchLengthProperty
            || change.Property == ReplacementTextProperty) Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
    }

    private IBrush? Find(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;

    private void Rebuild()
    {
        var text = SourceText ?? string.Empty;
        var start = Math.Clamp(MatchStart, 0, text.Length);
        var length = Math.Clamp(MatchLength, 0, text.Length - start);

        Inlines?.Clear();
        if (Inlines is null) return;

        var from = start > LeadingContext ? start - LeadingContext : 0;
        if (from > 0) Inlines.Add(new Run("…"));
        Inlines.Add(new Run(text[from..start]));

        if (length > 0)
        {
            var hasReplacement = ReplacementText is not null;
            var match = new Run(text.Substring(start, length)) { Background = Find(hasReplacement ? "SearchRemovedBrush" : "SearchMatchBrush") };
            if (hasReplacement) match.TextDecorations = [new TextDecoration { Location = TextDecorationLocation.Strikethrough }];
            Inlines.Add(match);
            if (!string.IsNullOrEmpty(ReplacementText)) Inlines.Add(new Run(ReplacementText) { Background = Find("SearchAddedBrush") });
        }
        Inlines.Add(new Run(text[(start + length)..]));
    }
}
