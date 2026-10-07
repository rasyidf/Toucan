using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>Drives the real window through the UI (typing, modes, dialogs, closing) rather than the view model alone.</summary>
public class AppSmokeTests
{
    private static void Pump() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static async Task<(TestHost Host, MainWindow Window, MainWindowViewModel Vm, string Folder)> OpenAsync(string name = "app")
    {
        var host = new TestHost();
        App.RegisterSidePanels();
        var folder = host.CreateJsonProject(name,
            ("en", """{"app.title": "My App", "buttons.save": "Save", "buttons.cancel": "Cancel"}"""),
            ("fr", """{"app.title": "Mon Application", "buttons.save": "Enregistrer"}"""));
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>());
        window.Show();
        await vm.OpenProjectAsync(folder);
        Pump();
        return (host, window, vm, folder);
    }

    private static List<TranslationCard> VisibleCards(Window w) =>
        w.GetVisualDescendants().OfType<TranslationCard>().Where(c => c.IsEffectivelyVisible).ToList();

    [AvaloniaFact]
    public async Task TypingIntoACard_MarksDirty_ShowsInTitle_AndSaves()
    {
        var (host, window, vm, folder) = await OpenAsync("typing");
        using var _ = host;

        var card = VisibleCards(window).Single(c => c.DataContext is LanguageGroupViewModel { Namespace: "buttons.cancel" });
        var box = card.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.DataContext is TranslationItemViewModel { Language: "fr" });
        Assert.True(box.Focus());
        Pump();
        window.KeyTextInput("Annuler");
        Pump();

        Assert.Equal("Annuler", box.Text);
        vm.FlushPendingEdits();
        Assert.True(vm.IsDirty);
        Assert.StartsWith("● ", window.Title, StringComparison.Ordinal);

        await vm.SaveCommand.ExecuteAsync(null);
        Pump();

        Assert.False(vm.IsDirty);
        Assert.DoesNotContain("●", window.Title, StringComparison.Ordinal);
        Assert.Contains("Annuler", File.ReadAllText(Path.Combine(folder, "fr.json")), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task EditorModes_AllRender_AndAuditIsReadOnly()
    {
        var (host, window, vm, _) = await OpenAsync("modes");
        using var _h = host;

        foreach (var mode in new[] { EditorMode.Review, EditorMode.Audit, EditorMode.Editor })
        {
            vm.EditorMode = mode;
            Pump();
            Assert.Equal(3, VisibleCards(window).Count);
            Assert.NotNull(window.CaptureRenderedFrame());
        }

        // PanelService is a process-wide singleton: always leave it in Editor mode for the next test.
        try
        {
            vm.EditorMode = EditorMode.Audit;
            Pump();
            var boxes = VisibleCards(window).SelectMany(c => c.GetVisualDescendants().OfType<TextBox>())
                .Where(t => t.DataContext is TranslationItemViewModel).ToList();
            Assert.NotEmpty(boxes);
            Assert.All(boxes, b => Assert.True(b.IsReadOnly || !b.IsEnabled));
        }
        finally
        {
            vm.EditorMode = EditorMode.Editor;
            Pump();
        }
    }

    [AvaloniaFact]
    public async Task ZenMode_ShowsSingleCard_AndExitsCleanly()
    {
        var (host, window, vm, _) = await OpenAsync("zen");
        using var _h = host;

        vm.ZenMode = true;
        Pump();
        Assert.NotNull(vm.ZenCurrentItem);
        Assert.NotNull(window.CaptureRenderedFrame());

        vm.ZenMode = false;
        Pump();
        Assert.Equal(3, VisibleCards(window).Count);
    }

    [AvaloniaFact]
    public async Task Search_NarrowsVisibleCards_AndClearingRestoresThem()
    {
        var (host, window, vm, _) = await OpenAsync("search");
        using var _h = host;

        vm.SearchText = "Enregistrer";
        await Task.Delay(400); // debounce
        Pump();
        Assert.Single(vm.PagingController.Data);
        Assert.Single(VisibleCards(window));

        vm.SearchText = string.Empty;
        await Task.Delay(400);
        Pump();
        Assert.Equal(3, VisibleCards(window).Count);
    }

    [AvaloniaFact]
    public async Task SearchPanel_SearchesAsYouType_GroupsByKey_AndReplaces()
    {
        var (host, window, vm, _) = await OpenAsync("panelsearch");
        using var _h = host;

        vm.OpenSearchPanelCommand.Execute(null);
        Pump();
        vm.MatchCase = false;
        vm.SearchQuery = "save";
        await Task.Delay(450); // panel debounce
        Pump();

        Assert.True(vm.HasSearchResults);
        var group = Assert.Single(vm.SearchGroups, g => g.Key == "buttons.save");
        Assert.True(group.Count >= 2); // the key itself plus the English value
        Assert.Contains(window.GetVisualDescendants().OfType<MatchTextBlock>(), t => t.IsEffectivelyVisible);

        vm.ShowReplacePanel = true;
        vm.ReplaceText = "Keep";
        Pump();
        Assert.All(group.Matches.Where(m => !m.InKey), m => Assert.Equal("Keep", m.Replacement));
        if (Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS") is { Length: > 0 } shots)
            window.CaptureRenderedFrame()?.Save(Path.Combine(shots, "search-panel-results.png"));

        await vm.ExecuteReplaceCommand.ExecuteAsync(null);
        Pump();
        Assert.Contains(vm.AllTranslation, t => t.Namespace == "buttons.save" && t.Language == "en" && t.Value == "Keep");

        vm.ClearSearchCommand.Execute(null);
        Assert.False(vm.HasSearchResults);
    }

    [AvaloniaFact]
    public async Task Close_ReturnsToStartScreen_AndProjectCanBeReopened()
    {
        var (host, window, vm, folder) = await OpenAsync("reopen");
        using var _h = host;

        await vm.CloseProjectCommand.ExecuteAsync(null);
        Pump();
        Assert.True(vm.ShowStartScreen);
        Assert.Empty(VisibleCards(window));

        await vm.OpenProjectAsync(folder);
        Pump();
        Assert.False(vm.ShowStartScreen);
        Assert.Equal(3, VisibleCards(window).Count);
    }

    [AvaloniaFact]
    public async Task CloseWithUnsavedChanges_HonoursCancelAndDontSave()
    {
        var (host, window, vm, folder) = await OpenAsync("closing");
        using var _h = host;
        ((ProjectLifecycleService)host.Services.GetRequiredService<IProjectLifecycleService>())
            .SetUnsavedChangesHandler(new UnsavedChangesHandler(host.Messages));

        vm.PagingController.Data.Single(g => g.Namespace == "buttons.cancel")
            .Translations.Single(t => t.Language == "fr").Value = "Annuler";
        vm.FlushPendingEdits();
        Assert.True(vm.IsDirty);

        host.Messages.ChoiceAnswer = ChoiceResult.Cancel;
        Assert.False(await vm.TryCloseProjectAsync());
        Assert.True(vm.HasProject);

        host.Messages.ChoiceAnswer = ChoiceResult.Secondary; // Don't Save
        Assert.True(await vm.TryCloseProjectAsync());
        Assert.DoesNotContain("Annuler", File.ReadAllText(Path.Combine(folder, "fr.json")), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Dialogs_AllLoadAndRender()
    {
        var (host, window, vm, _) = await OpenAsync("dialogs");
        using var _h = host;

        var dialogs = new List<Window>
        {
            new PromptDialog("Title", "Message", "default"),
            new PickDialog("Title", "Message", ["a", "b"], "a"),
            new OptionsDialog(host.Services.GetRequiredService<OptionsViewModel>()),
            new StatisticsDialog(new StatisticsViewModel(vm.AllTranslation)),
            new ManageLanguagesDialog(new LanguageManagerViewModel(vm.AllTranslation, "en")),
            new NewProjectDialog(new NewProjectViewModel()),
        };

        foreach (var dialog in dialogs)
        {
            dialog.Show(window);
            Pump();
            Assert.True(dialog.IsVisible, dialog.GetType().Name);
            Assert.NotNull(dialog.CaptureRenderedFrame());
            dialog.Close();
            Pump();
        }
    }

    [AvaloniaFact]
    public async Task OptionsDialog_EveryPageRenders()
    {
        var (host, window, _, _) = await OpenAsync("options");
        using var _h = host;

        var optionsVm = host.Services.GetRequiredService<OptionsViewModel>();
        var dialog = new OptionsDialog(optionsVm);
        dialog.Show(window);
        for (var i = 0; i < OptionsViewModel.Pages.Count; i++)
        {
            optionsVm.SelectedPageIndex = i;
            Pump();
            Assert.NotNull(dialog.CaptureRenderedFrame());
        }
        dialog.Close();
    }
}
