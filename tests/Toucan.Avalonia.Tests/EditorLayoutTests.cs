using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Toucan.Core.Contracts;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class EditorLayoutTests
{
    [AvaloniaTheory]
    [InlineData("FutureLayout")]
    [InlineData("999")]
    public void UnknownSavedLayoutFallsBackToCards(string layout)
    {
        using var host = new TestHost();
        host.Services.GetRequiredService<IPreferenceService>().Load().EditorLayout = layout;
        Assert.True(host.CreateViewModel().IsCardLayout);
    }

    [AvaloniaFact]
    public async Task SwitchingLayouts_FlushesEditsPreservesSelectionAndRemembersChoice()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("layouts", ("en", """{"first":"Hello","second":"Bye"}"""),
            ("fr", """{"first":"Bonjour","second":"Salut"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        var selected = vm.PagingController.Data[1];
        vm.SelectedGroup = selected;
        var item = selected.Translations.Single(t => t.Language == "fr");
        item.Value = "Au revoir";
        vm.SetEditorLayoutCommand.Execute(EditorLayout.Rows);
        Assert.Equal("Au revoir", item.Model.Value);
        Assert.Same(selected, vm.SelectedGroup);
        Assert.True(vm.IsRowLayout);
        Assert.Equal("Rows", host.Services.GetRequiredService<IPreferenceService>().Load().EditorLayout);
        using (var restarted = new MainWindowViewModel(host.Services.GetRequiredService<MainWindowServices>()))
            Assert.Equal(EditorLayout.Rows, restarted.EditorLayout);

        vm.ToggleFocusedEditorCommand.Execute(null);
        Assert.True(vm.FocusedEditorMode);
        Assert.Same(selected, vm.ZenCurrentItem);
        vm.ToggleFocusedEditorCommand.Execute(null);
        Assert.True(vm.IsRowLayout);
        Assert.Same(selected, vm.SelectedGroup);
        Assert.Contains(selected, vm.PageData);
        vm.SetEditorLayoutCommand.Execute(EditorLayout.Cards);
        Assert.True(vm.IsCardLayout);
        Assert.Same(selected, vm.SelectedGroup);
    }

    [AvaloniaTheory]
    [InlineData(880, false)]
    [InlineData(1280, true)]
    public async Task Rows_RenderColumnsWhenTheyFitAndStackOtherwise(int width, bool dark)
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var theme = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var folder = host.CreateJsonProject("row-layout", ("en", """{"app.greeting":"Hello {{name}}\nWelcome back","items_one":"One item","items_other":"Many items"}"""),
            ("fr", """{"app.greeting":"Bonjour {{name}}\nBon retour","items_one":"Un article","items_other":"Des articles"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = width, Height = 800 };
        try
        {
            window.Show();
            await vm.OpenProjectAsync(folder);
            vm.SetEditorLayoutCommand.Execute(EditorLayout.Rows);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var cards = window.GetVisualDescendants().OfType<TranslationCard>().Where(c => c.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
            {
                Assert.Equal(width == 1280, card.Classes.Contains("columns"));
                var boxes = card.GetVisualDescendants().OfType<TextBox>().Where(b => b.Classes.Contains("translation") && b.IsEffectivelyVisible).ToList();
                foreach (var box in boxes)
                {
                    var point = Assert.NotNull(box.TranslatePoint(default, card));
                    Assert.True(box.Bounds.Width >= 150);
                    Assert.True(point.X >= 0 && point.X + box.Bounds.Width <= card.Bounds.Width + 1);
                }
                var first = Assert.NotNull(boxes[0].TranslatePoint(default, card));
                var second = Assert.NotNull(boxes[1].TranslatePoint(default, card));
                if (width == 1280) Assert.True(second.X > first.X + 100);
                else Assert.True(second.Y > first.Y);
            }
            var directory = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"rows-{width}.png"));
            }
            var picker = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("editorLayoutPicker"));
            var toolbar = picker.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("filterToolbar"));
            var pickerPoint = Assert.NotNull(picker.TranslatePoint(default, toolbar));
            Assert.True(pickerPoint.Y >= 0 && pickerPoint.Y + picker.Bounds.Height <= toolbar.Bounds.Height + 1);
            var ordinary = cards.Single(c => c.DataContext is LanguageGroupViewModel { IsPluralGroup: false });
            var previousWidth = ordinary.GetVisualDescendants().OfType<TextBox>().First(b => b.Classes.Contains("translation")).Bounds.Width;
            vm.LanguageVisibilityFilter.Single(l => l.Language == "fr").IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var visible = Assert.Single(ordinary.GetVisualDescendants().OfType<TextBox>(), b => b.Classes.Contains("translation") && b.IsEffectivelyVisible);
            Assert.Equal("en", Assert.IsType<TranslationItemViewModel>(visible.DataContext).Language);
            if (width == 1280) Assert.True(visible.Bounds.Width > previousWidth * 1.5);
            Assert.True(ordinary.FocusEditor());
            vm.SwitchToAuditModeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(visible.IsReadOnly);
            Assert.False(ordinary.FocusEditor());
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = theme;
        }
    }
}
