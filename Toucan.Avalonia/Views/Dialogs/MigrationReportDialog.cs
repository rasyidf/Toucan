using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Toucan.Avalonia.Locales;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;

namespace Toucan.Avalonia.Views.Dialogs;

public sealed class MigrationReportDialog : DialogWindow
{
    private static readonly System.Text.Json.JsonSerializerOptions ReportJson = new() { WriteIndented = true };
    public MigrationReportDialog(MigrationReport report, bool canApply, IDialogService dialogs)
    {
        Title = Loc.T("Migration report"); Width = 800; Height = 600;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(12) };
        var copy = new Button { Content = Loc.T("Copy report") }; copy.Click += async (_, _) => await PlatformService.SetClipboardTextAsync(report.Summary); footer.Children.Add(copy);
        var export = new Button { Content = Loc.T("Export report") }; export.Click += async (_, _) =>
        {
            var path = await dialogs.SaveFileAsync(null, "toucan-migration-report.json", Loc.T("Export report"));
            if (path != null)
                try { await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(report, ReportJson)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { export.Content = ex.Message; }
        }; footer.Children.Add(export);
        var cancel = new Button { Content = Loc.T(canApply ? "Cancel" : "Close") }; cancel.Click += (_, _) => Close(false); footer.Children.Add(cancel);
        if (canApply) { var apply = new Button { Content = Loc.T("Open migrated project") }; apply.Click += (_, _) => Close(true); footer.Children.Add(apply); }
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = new DockPanel { Children = { footer, new ScrollViewer { Content = new SelectableTextBlock { Text = report.Summary, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(16) } } } };
    }
}
