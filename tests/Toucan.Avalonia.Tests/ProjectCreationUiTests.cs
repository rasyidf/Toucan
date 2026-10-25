using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Dialogs;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class ProjectCreationUiTests
{
    private static void Pump()
    {
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task WelcomeTemplateClick_PassesTheChosenFrameworkToTheWizard()
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>());
        try
        {
            window.Show();
            Pump();
            var shortcut = window.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Classes.Contains("templateTile") && b.DataContext is FrameworkTile { Name: "Android" });
            shortcut.BringIntoView();
            Pump();
            var point = Assert.NotNull(shortcut.TranslatePoint(new Point(shortcut.Bounds.Width / 2, shortcut.Bounds.Height / 2), window));
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Pump();
            if (vm.NewFolderCommand.ExecutionTask is { } execution) await execution;
            Assert.Equal("Android", host.Dialogs.RequestedFramework);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ShortWizard_AllTemplatesAreReachable_AndSelectionSurvivesBack()
    {
        using var host = new TestHost();
        var vm = host.Services.GetRequiredService<NewProjectViewModel>();
        vm.ProjectFolder = Path.Combine(host.Root, "new-project");
        var dialog = new NewProjectDialog(vm) { Width = 800, Height = 520 };
        try
        {
            dialog.Show();
            Pump();
            var selector = dialog.FindControl<ListBox>("FrameworkSelector")!;
            selector.SelectedItem = vm.Frameworks.Last();
            var last = selector.GetVisualDescendants().OfType<ListBoxItem>().Last();
            last.BringIntoView();
            Pump();
            var scroller = dialog.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.ScrollBarMaximum.Y > 0);
            Assert.True(scroller.Offset.Y > 0);
            var next = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsVisible && b.IsDefault);
            var location = Assert.NotNull(next.TranslatePoint(default, dialog));
            Assert.True(location.Y + next.Bounds.Height <= dialog.ClientSize.Height);
            await vm.NextStepCommand.ExecuteAsync(null);
            Pump();
            Assert.True(vm.IsStep1);
            Assert.Equal("Step 2 of 2", dialog.FindControl<TextBlock>("StepText")!.Text);
            Assert.True(dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsVisible && b.IsDefault).Command == vm.CreateCommand);
            vm.PreviousStepCommand.Execute(null);
            Assert.Equal("CSV", vm.SelectedFramework!.Name);
        }
        finally { dialog.Close(); }
    }
}
