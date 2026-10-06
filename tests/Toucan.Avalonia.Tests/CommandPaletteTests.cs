using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class CommandPaletteTests
{
    private static PaletteCommand Cmd(string category, string title, bool canRun = true) =>
        new(category, title, null, new RelayCommand(() => { }, () => canRun));

    [Fact]
    public void Rank_PutsTitlePrefixFirst_ThenWordStart_ThenAnywhere()
    {
        var all = new[] { Cmd("File", "Close Project"), Cmd("View", "Toggle Left Panel"), Cmd("File", "Save As…"), Cmd("File", "Save"), Cmd("Edit", "Delete Key…") };

        var titles = CommandPalette.Rank(all, "sav").Select(c => c.Title).ToList();

        Assert.Equal(["Save As…", "Save"], titles);   // both are prefixes: menu order is the tie-break
        Assert.Equal(["Toggle Left Panel"], CommandPalette.Rank(all, "left").Select(c => c.Title));
        Assert.Equal(["Close Project"], CommandPalette.Rank(all, "file project").Select(c => c.Title)); // category counts, every word must match
        Assert.Empty(CommandPalette.Rank(all, "zzz"));
    }

    [Fact]
    public void Rank_SinksCommandsThatCannotRun_AndKeepsMenuOrderWithoutAQuery()
    {
        var all = new[] { Cmd("File", "Save", canRun: false), Cmd("File", "Save As…"), Cmd("File", "Open Folder…") };

        Assert.Equal(["Save As…", "Save"], CommandPalette.Rank(all, "save").Select(c => c.Title));
        Assert.Equal(["Save As…", "Open Folder…", "Save"], CommandPalette.Rank(all, "").Select(c => c.Title));
    }

    [AvaloniaFact]
    public async Task Palette_OpensWithEveryMenuCommand_FiltersAsYouType_AndClosesOnEscape()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("pal", ("en", """{"a": "x"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenProjectAsync(folder);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var palette = window.GetVisualDescendants().OfType<CommandPalette>().Single();
        Assert.False(palette.IsVisible);

        vm.ToggleCommandPaletteCommand.Execute(null);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(palette.IsVisible);

        var list = palette.GetVisualDescendants().OfType<ListBox>().Single();
        var input = palette.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.True(list.ItemCount > 40, "every runnable menu item should be listed");

        input.Text = "pre-trans";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("Pre-translate…", Assert.IsType<PaletteCommand>(list.SelectedItem).Title);
        Assert.Equal("Translate", ((PaletteCommand)list.SelectedItem!).Category);
        Assert.False(string.IsNullOrEmpty(((PaletteCommand)list.SelectedItem).Shortcut));

        // Escape closes it wherever focus is: the window-level handler owns the key while the palette is open.
        var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        KeybindingService.HandleZenKeys(window, escape, vm);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(escape.Handled);
        Assert.False(vm.IsCommandPaletteOpen);
        Assert.False(palette.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SaveChip_FollowsTheDirtyState_AndTheShortcutIsBound()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("chip", ("en", """{"a": "x"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        window.Show();
        await vm.OpenProjectAsync(folder);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var chip = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("saveState"));
        Assert.True(chip.IsVisible);
        Assert.DoesNotContain("dirty", chip.Classes);

        vm.IsDirty = true;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("dirty", chip.Classes);

        Assert.Contains(window.KeyBindings, b => b.Command == vm.ToggleCommandPaletteCommand || b.Command?.GetType().Name == "TextBoxGuardCommand");
        Assert.NotNull(KeybindingService.GestureFor("Command Palette"));
        window.Close();
    }
}
