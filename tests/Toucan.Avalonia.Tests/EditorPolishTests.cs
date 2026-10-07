using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Toucan.Avalonia.Views.Dialogs;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class EditorPolishTests
{
    [AvaloniaFact]
    public async Task FocusedEditor_ClampsAfterFilteringAndReturnsToCurrentPage()
    {
        using var host = new TestHost();
        var json = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 40).ToDictionary(i => $"key{i:D2}", i => $"Value {i}"));
        var folder = host.CreateJsonProject("focus", ("en", json));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.SelectedGroup = vm.PagingController.Data[2];
        vm.ToggleFocusedEditorCommand.Execute(null);
        Assert.Equal(2, vm.FocusedIndex);
        vm.FocusedIndex = 20;
        Assert.Same(vm.ZenCurrentItem, vm.SelectedGroup);
        vm.ToggleFocusedEditorCommand.Execute(null);
        Assert.Contains(vm.SelectedGroup, vm.PageData);
        Assert.Equal(2, vm.PagingController.Page);

        vm.ToggleFocusedEditorCommand.Execute(null);
        vm.SearchText = "key05";
        vm.ApplySearchCommand.Execute(null);
        Assert.Single(vm.PagingController.Data);
        Assert.Equal(0, vm.FocusedIndex);
        Assert.False(vm.FocusedPreviousCommand.CanExecute(null));
        Assert.False(vm.FocusedNextCommand.CanExecute(null));
        Assert.Same(vm.ZenCurrentItem, vm.SelectedGroup);
        Assert.Equal("1 / 1", vm.FocusedPositionText);
        vm.SearchText = "no-match-at-all";
        vm.ApplySearchCommand.Execute(null);
        Assert.Null(vm.ZenCurrentItem);
        Assert.Equal("0 / 0", vm.FocusedPositionText);
        Assert.False(vm.FocusedNextCommand.CanExecute(null));
        Assert.False(vm.FocusedPreviousCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(880, false, EditorMode.Editor)]
    [InlineData(1280, true, EditorMode.Editor)]
    [InlineData(880, true, EditorMode.Review)]
    [InlineData(880, true, EditorMode.Audit)]
    public async Task FocusedEditor_RendersWithinNarrowAndWideWindows(int width, bool dark, EditorMode mode)
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var oldTheme = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var folder = host.CreateJsonProject("polish", ("en", """{"a.really.long.key.for.wrapping": "First line\nSecond line with a {name} placeholder", "second": "Next key"}"""),
            ("de-DE", """{"a.really.long.key.for.wrapping": "Ein langer übersetzter Wert", "second": "Nächster Schlüssel"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = width };
        try
        {
            window.Show();
            await vm.OpenProjectAsync(folder);
            if (mode == EditorMode.Review) vm.SwitchToReviewModeCommand.Execute(null);
            if (mode == EditorMode.Audit) vm.SwitchToAuditModeCommand.Execute(null);
            vm.ToggleFocusedEditorCommand.Execute(null);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var cards = window.GetVisualDescendants().OfType<TranslationCard>().Where(c => c.IsEffectivelyVisible).ToList();
            var card = Assert.Single(cards);
            Assert.True(card.Bounds.Width > 250);
            foreach (var box in card.GetVisualDescendants().OfType<TextBox>().Where(b => b.Classes.Contains("translation")))
            {
                var point = box.TranslatePoint(default, card)!.Value;
                Assert.True(point.X >= 0);
                Assert.True(point.X + box.Bounds.Width <= card.Bounds.Width + 1);
                Assert.True(box.Bounds.Width >= 150);
                if (card.Classes.Contains("narrow")) Assert.True(box.Bounds.Width >= card.Bounds.Width - 28);
            }
            SaveShot(window, $"focused-{width}-{(dark ? "dark" : "light")}-{mode}");
            vm.FocusedNextCommand.Execute(null);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Same(vm.ZenCurrentItem, vm.SelectedGroup);
            vm.ToggleFocusedEditorCommand.Execute(null);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            SaveShot(window, $"list-{width}-{(dark ? "dark" : "light")}-{mode}");
        }
        finally
        {
            window.Hide();
            Application.Current.RequestedThemeVariant = oldTheme;
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(2)]
    public void SettingsRows_RenderFullWidthWithoutExtraStackedGap(int page)
    {
        using var host = new TestHost();
        var vm = host.Services.GetRequiredService<OptionsViewModel>();
        vm.RecentItems.Add(new Toucan.Core.Models.Project { Path = "/Users/developer/projects/a-long-path/translation-project" });
        vm.SelectedPageIndex = page;
        var window = new OptionsDialog(vm);
        try
        {
            window.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var row in window.GetVisualDescendants().OfType<SettingsRow>().Where(r => r.Stacked && r.IsEffectivelyVisible))
            {
                var presenter = row.GetVisualDescendants().OfType<ContentPresenter>().Single(c => c.Name == "PART_ContentPresenter" && c.TemplatedParent == row);
                Assert.True(presenter.Bounds.Width >= row.Bounds.Width - row.Padding.Left - row.Padding.Right - 3);
            }
            SaveShot(window, $"settings-{page}");
            var toggle = window.GetVisualDescendants().OfType<ToggleSwitch>().First(t => t.IsEffectivelyVisible);
            Assert.True(toggle.Bounds.Height <= 28);
            Assert.True(toggle.Bounds.Width <= 56);
            var track = toggle.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "OuterBorder");
            var trackTop = track.TranslatePoint(default, toggle)!.Value.Y;
            Assert.True(trackTop >= 0 && trackTop + track.Bounds.Height <= toggle.Bounds.Height);
        }
        finally { window.Close(); }
    }

    private static void SaveShot(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        window.CaptureRenderedFrame()!.Save(Path.Combine(directory, name + ".png"));
    }
}
