using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class EditorAccessTests
{
    [AvaloniaTheory]
    [InlineData(Key.F2)]
    [InlineData(Key.Enter)]
    public async Task SelectedCard_StartsEditingVisibleTargetAndPreservesLanguage(Key key)
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("access", ("en", """{"first":"Hello", "second":"Goodbye"}"""),
            ("fr", """{"first":"Bonjour", "second":"Au revoir"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            await vm.OpenProjectAsync(folder);
            vm.ProjectSettings!.PrimaryLanguage = "en";
            Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(b => b.Name == "CardList");
            list.SelectedIndex = 0;
            Assert.True(list.ContainerFromIndex(list.SelectedIndex)!.Focus());
            window.KeyPress(key, RawInputModifiers.None, key == Key.F2 ? PhysicalKey.F2 : PhysicalKey.Enter, null);
            window.KeyRelease(key, RawInputModifiers.None, key == Key.F2 ? PhysicalKey.F2 : PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            var first = Assert.Single(list.GetVisualDescendants().OfType<TextBox>(), b => b.IsFocused);
            Assert.Equal("fr", Assert.IsType<TranslationItemViewModel>(first.DataContext).Language);
            Assert.Equal(first.Text!.Length, first.CaretIndex);

            list.SelectedIndex = 1;
            Assert.True(list.ContainerFromIndex(list.SelectedIndex)!.Focus());
            window.KeyPress(key, RawInputModifiers.None, key == Key.F2 ? PhysicalKey.F2 : PhysicalKey.Enter, null);
            window.KeyRelease(key, RawInputModifiers.None, key == Key.F2 ? PhysicalKey.F2 : PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            var next = Assert.Single(list.GetVisualDescendants().OfType<TextBox>(), b => b.IsFocused);
            var item = Assert.IsType<TranslationItemViewModel>(next.DataContext);
            Assert.Equal("fr", item.Language);
            Assert.Same(item, vm.FocusedTranslationItem);
            Assert.Contains(item, vm.SelectedGroup!.AllItems);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Contains("\n", next.Text!);
            Assert.True(next.IsFocused);
            var directory = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, "active-field.png"));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LanguageBadge_FocusesItsFieldAndAuditStaysReadOnly()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject("badge", ("en", """{"greeting":"Hello"}"""), ("fr", """{"greeting":"Bonjour"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            await vm.OpenProjectAsync(folder);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var card = window.GetVisualDescendants().OfType<TranslationCard>().First(c => c.IsEffectivelyVisible);
            var badge = card.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("rowLanguage") &&
                b.DataContext is TranslationItemViewModel { Language: "fr" });
            var point = Assert.NotNull(badge.TranslatePoint(new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), window));
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var box = card.GetVisualDescendants().OfType<TextBox>().Single(b => b.Classes.Contains("translation") && b.IsFocused);
            Assert.Equal("fr", Assert.IsType<TranslationItemViewModel>(box.DataContext).Language);
            Assert.True(vm.FocusedTranslationItem!.ShowCount);

            vm.SwitchToAuditModeCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(card.FocusEditor());
            Assert.All(card.GetVisualDescendants().OfType<TextBox>().Where(b => b.Classes.Contains("translation")), b => Assert.True(b.IsReadOnly));
        }
        finally { window.Close(); }
    }
}
