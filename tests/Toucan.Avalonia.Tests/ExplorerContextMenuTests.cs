using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Panels;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class ExplorerContextMenuTests
{
    [AvaloniaFact]
    public async Task RightClickOnADocumentEntrySelectsItsIdentityAndOpensTheKeyMenu()
    {
        using var host = new TestHost(); App.RegisterSidePanels();
        var folder = Path.Combine(host.Root, "document-context");
        Directory.CreateDirectory(Path.Combine(folder, "locales/en"));
        File.WriteAllText(Path.Combine(folder, "locales/en/common.json"), "{\"title\":\"Hello\"}");
        new Toucan.Core.Models.ProjectSettings { ProjectPath = folder, Framework = "i18next", SaveFormat = Toucan.Core.Models.FormatIds.Namespaced,
            PrimaryLanguage = "en", Languages = ["en"] }.Save();
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenProjectAsync(folder);
        Toucan.Core.Services.SidePanelRegistry.Instance.Activate("explorer");
        foreach (var root in vm.DocumentNavigationItems) root.IsExpanded = true;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var panel = window.GetVisualDescendants().OfType<ExplorerPanel>().First();
        var tree = panel.GetVisualDescendants().OfType<TreeView>().First(t => t.IsEffectivelyVisible);
        var item = tree.GetVisualDescendants().OfType<TreeViewItem>().Single(t => t.DataContext is WorkspaceNavigationItemViewModel { IsEntry: true });
        var entry = Assert.IsType<WorkspaceNavigationItemViewModel>(item.DataContext);
        var point = Assert.NotNull(item.TranslatePoint(new Point(40, 10), window));
        window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var flyout = Assert.IsType<MenuFlyout>(panel.LastFlyout); Assert.True(flyout.IsOpen);
        Assert.Equal(Assert.Single(entry.Node.EntryIds), vm.SelectedGroup!.EntryId);
        Assert.Contains(flyout.Items.OfType<MenuItem>(), m => m.Header?.ToString() == "Rename…");
        Assert.Contains(flyout.Items.OfType<MenuItem>(), m => m.Header?.ToString() == "Delete…");
        window.Close();
    }

    [AvaloniaFact]
    public async Task RightClickOnAKey_OpensTheKeyMenu()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("ctx", ("en", """{"a.b": "x", "c.d": "y"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenProjectAsync(folder);
        Toucan.Core.Services.SidePanelRegistry.Instance.Activate("explorer");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // lays the tree out before we aim at a row
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var tree = window.GetVisualDescendants().OfType<ExplorerPanel>().First().GetVisualDescendants().OfType<TreeView>()
            .First(t => t.IsEffectivelyVisible);
        var item = tree.GetVisualDescendants().OfType<TreeViewItem>().First();
        var point = Assert.NotNull(item.TranslatePoint(new Point(40, 10), window));
        Assert.True(item.Bounds.Height > 0, "the tree row has not been laid out");

        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var panel = window.GetVisualDescendants().OfType<ExplorerPanel>().First();
        var flyout = Assert.IsType<MenuFlyout>(panel.LastFlyout);
        Assert.True(flyout.IsOpen);
        var headers = flyout.Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();
        Assert.Contains("Add Key…", headers);
        Assert.Contains("Rename…", headers);
        Assert.Contains("Delete…", headers);
        window.Close();
    }
}
