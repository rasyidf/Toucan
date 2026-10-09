using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Toucan.Plugins.Desktop;

namespace Toucan.Sample.Plugin.Desktop;

/// <summary>
/// Entry point of the sample's desktop part. Toucan creates it after the main plugin has loaded, in the desktop app only.
/// Contribution IDs start with the plugin ID, like command IDs.
/// </summary>
public sealed class SampleDesktopPlugin : IToucanDesktopPlugin
{
    private const string StampCommand = "sample.tsv.stamp";
    private const string AboutDialog = "sample.tsv.about";

    public void InitializeDesktop(IDesktopPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var workspace = context.Workspace;
        var host = context.Host;

        // A panel in the right side bar. Its toolbar button runs a command, so it follows that command's state.
        context.AddSidePanel(new SidePanelContribution
        {
            Id = "sample.tsv.panel",
            Title = "Sample",
            LocalizedTitles = new Dictionary<string, string> { ["id"] = "Contoh" },
            Slot = PanelSlot.Right,
            Icon = "Edit",
            Actions = [new PanelAction(StampCommand, "Edit", "Write sample stamp")],
            CreateContent = ws =>
            {
                var project = new TextBlock { TextWrapping = TextWrapping.Wrap };
                var key = new TextBlock { TextWrapping = TextWrapping.Wrap };
                void Refresh()
                {
                    project.Text = ws.HasWorkspace ? $"Project: {ws.ProjectPath}" : "No project open.";
                    key.Text = ws.SelectedKey is { } k ? $"Selected key: {k}" : "No key selected.";
                }
                ws.Changed += (_, _) => Refresh();
                Refresh();

                var stamp = new Button { Content = "Write sample stamp" };
                stamp.Click += async (_, _) => await ws.ExecuteCommandAsync(StampCommand);
                var about = new Button { Content = "About this plugin" };
                about.Click += async (_, _) => await host.ShowDialogAsync(AboutDialog);

                return new StackPanel { Margin = new Avalonia.Thickness(12), Spacing = 8, Children = { project, key, stamp, about } };
            },
        });

        // A section at the bottom of the inspector.
        context.AddInspectorSection(new InspectorSectionContribution
        {
            Id = "sample.tsv.inspector",
            Title = "Sample",
            CreateContent = ws =>
            {
                var text = new TextBlock { Margin = new Avalonia.Thickness(10, 8), TextWrapping = TextWrapping.Wrap };
                text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(DesktopTheme.MutedTextBrush));
                void Refresh() => text.Text = ws.SelectedKey is { } k ? $"The sample plugin sees “{k}”." : "No key selected.";
                ws.Changed += (_, _) => Refresh();
                Refresh();
                return text;
            },
        });

        // A group of settings on the Plugins page of Settings.
        context.AddSettingsPage(new SettingsPageContribution
        {
            Id = "sample.tsv.settings",
            Title = "Sample plugin",
            Description = "Settings the plugin offers. Saving them arrives with the host services in a later release.",
            CreateContent = _ => new TextBlock { Text = "Nothing to configure yet.", Margin = new Avalonia.Thickness(16, 8, 16, 16), FontStyle = FontStyle.Italic },
        });

        context.AddDialog(new DialogContribution
        {
            Id = AboutDialog,
            Title = "About the sample plugin",
            Width = 420,
            CreateContent = (_, _) => new TextBlock
            {
                Text = "The sample adds a tab-separated format, a TODO check and these screens. It is a template for your own plugin.",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            },
        });

        // Shown in the inspector's key actions and in the key context menus; the command receives the key's name.
        context.AddEditorAction(new EditorActionContribution
        {
            Id = "sample.tsv.stamp-action",
            Title = "Stamp this key",
            CommandId = StampCommand,
            Icon = "Edit",
        });
    }
}
