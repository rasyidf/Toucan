using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Toucan.Core.Services;

namespace Toucan.Avalonia.Views.Dialogs;

/// <summary>Read-only review; conflicts block acceptance of the entire workbook.</summary>
public sealed class SpreadsheetPreviewDialog : DialogWindow
{
    public SpreadsheetPreviewDialog(SpreadsheetPreview preview)
    {
        Title = "Spreadsheet import preview";
        Width = 800; Height = 580;
        var apply = new Button { Content = "Apply changes", IsEnabled = preview.CanApply, IsDefault = true };
        apply.Classes.Add("accent");
        apply.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16), Spacing = 8, Children = { cancel, apply } };
        DockPanel.SetDock(footer, Dock.Bottom);
        var title = new TextBlock { Text = preview.Summary + "\nAbsent columns and 'present=false' units stay unchanged. Present blank cells clear values. Conflicts must be fixed before importing.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
        DockPanel.SetDock(title, Dock.Top);
        var lines = preview.Conflicts.Select(c => "Conflict: " + c).Concat(preview.Changes.Select(c =>
            $"Row {c.Row} | {c.Kind} | {c.Package}/{c.Key} [{c.Language}]\n{c.Before} → {c.After}\nComment: {c.BeforeComment} → {c.AfterComment}; approved: {c.BeforeApproved} → {c.AfterApproved}")).ToList();
        Content = new DockPanel { Children = { footer, title, new ListBox { ItemsSource = lines, Margin = new Thickness(16, 0, 16, 16) } } };
    }
}
