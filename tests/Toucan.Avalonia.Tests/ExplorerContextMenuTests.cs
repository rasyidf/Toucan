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

        var tree = window.GetVisualDescendants().OfType<ExplorerPanel>().First().GetVisualDescendants().OfType<TreeView>().First();
        var item = tree.GetVisualDescendants().OfType<TreeViewItem>().First();
        var point = item.TranslatePoint(new Point(40, 10), window) ?? default;

        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var menu = Assert.IsType<ContextMenu>(tree.ContextMenu);
        Assert.True(menu.IsOpen);
        var headers = menu.Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).ToList();
        Assert.Contains("Add Key…", headers);
        Assert.Contains("Rename…", headers);
        Assert.Contains("Delete…", headers);
        window.Close();
    }
}
