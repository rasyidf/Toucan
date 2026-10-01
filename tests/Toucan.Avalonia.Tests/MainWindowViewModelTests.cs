using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>End-to-end flows through the real services, against a temporary project folder.</summary>
public class MainWindowViewModelTests
{
    private const string En = """{"app.title": "My App", "buttons.save": "Save", "buttons.cancel": "Cancel"}""";
    private const string Fr = """{"app.title": "Mon Application", "buttons.save": "Enregistrer"}""";

    private static Dictionary<(string Ns, string Lang), string> Reload(TestHost host, string folder) =>
        host.Services.GetRequiredService<IProjectService>().LoadProject(folder).Translations
            .GroupBy(t => (t.Namespace, t.Language)).ToDictionary(g => g.Key, g => g.First().Value);

    [AvaloniaFact]
    public async Task Open_LoadsKeysAndLanguages_AndFillsMissingEntries()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("open", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();

        await vm.OpenProjectAsync(folder);

        Assert.True(vm.HasProject);
        Assert.False(vm.ShowStartScreen);
        Assert.Equal(3, vm.PagingController.Data.Count);
        Assert.Equal(["en", "fr"], vm.ProjectLanguages().Order());
        // fr had no buttons.cancel; it's materialized as an empty entry so every card shows every language.
        Assert.Contains(vm.AllTranslation, t => t.Namespace == "buttons.cancel" && t.Language == "fr" && t.Value.Length == 0);
        Assert.False(vm.IsDirty);
    }

    [AvaloniaFact]
    public async Task EditingAValue_MarksDirty_AndSaveWritesIt()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("edit", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        var card = vm.PagingController.Data.Single(g => g.Namespace == "buttons.cancel");
        var fr = card.Translations.Single(t => t.Language == "fr");
        fr.Value = "Annuler";
        vm.FlushPendingEdits();

        Assert.True(vm.IsDirty);
        Assert.Contains("buttons.cancel", vm.SessionDirtyKeys);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.IsDirty);
        Assert.Equal("Annuler", Reload(host, folder)[("buttons.cancel", "fr")]);
    }

    [AvaloniaFact]
    public async Task NewKey_IsCreatedForEveryLanguage_AndPersisted()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("newkey", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        await vm.CreateNewItemAsync("errors.not_found");
        var enItem = vm.AllTranslation.Single(t => t.Namespace == "errors.not_found" && t.Language == "en");
        enItem.Value = "Not found";
        vm.AllTranslation.Single(t => t.Namespace == "errors.not_found" && t.Language == "fr").Value = "Introuvable";
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Reload(host, folder);
        Assert.Equal("Not found", saved[("errors.not_found", "en")]);
        Assert.Equal("Introuvable", saved[("errors.not_found", "fr")]);
    }

    [AvaloniaFact]
    public async Task DeletedKey_DoesNotComeBackAfterSave()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("delete", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        vm.DeleteNamespaces(["buttons.save"]);
        Assert.True(vm.IsDirty);
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Reload(host, folder);
        Assert.DoesNotContain(saved.Keys, k => k.Ns == "buttons.save");
        Assert.Contains(saved.Keys, k => k.Ns == "buttons.cancel");
    }

    [AvaloniaFact]
    public async Task RenameSegment_MovesAllNestedKeys()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("rename", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        var error = vm.RenameItemCore(new Toucan.Core.Models.NsTreeItem { Namespace = "buttons", Name = "buttons" }, "actions");
        Assert.Null(error);
        await vm.SaveCommand.ExecuteAsync(null);

        var saved = Reload(host, folder);
        Assert.Equal("Enregistrer", saved[("actions.save", "fr")]);
        Assert.DoesNotContain(saved.Keys, k => k.Ns.StartsWith("buttons.", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Undo_RestoresPreviousValue()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("undo", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        var en = vm.PagingController.Data.Single(g => g.Namespace == "app.title").Translations.Single(t => t.Language == "en");
        en.Value = "Renamed App";
        vm.FlushPendingEdits();
        vm.UndoCommand.Execute(null);

        Assert.Equal("My App", vm.AllTranslation.Single(t => t.Namespace == "app.title" && t.Language == "en").Value);
    }

    [AvaloniaFact]
    public async Task KeyPathFilter_ShowsOnlyThatBranch()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("filter", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        vm.SearchText = "buttons.";
        vm.ApplySearchCommand.Execute(null);

        Assert.Equal(["buttons.cancel", "buttons.save"], vm.PagingController.Data.Select(g => g.Namespace).Order());
    }

    [AvaloniaFact]
    public async Task ReviewMode_ShowsOnlyUntranslatedOrUnapproved()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("review", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        foreach (var t in vm.AllTranslation.Where(t => t.Namespace == "app.title")) t.IsApproved = true;

        vm.SwitchToReviewModeCommand.Execute(null);
        try
        {
            Assert.DoesNotContain(vm.PagingController.Data, g => g.Namespace == "app.title");
            Assert.Contains(vm.PagingController.Data, g => g.Namespace == "buttons.cancel");
        }
        finally
        {
            vm.SwitchToEditorModeCommand.Execute(null);
        }
    }

    [AvaloniaFact]
    public async Task Validation_FindsPlaceholderMismatch()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("validate",
            ("en", """{"greeting": "Hello {{name}}"}"""),
            ("fr", """{"greeting": "Bonjour"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);

        vm.RunValidationCommand.Execute(null);

        Assert.Contains(vm.ValidationIssues, i => i.Namespace == "greeting" && i.Language == "fr");
    }

    [AvaloniaFact]
    public async Task CloseProject_WithUnsavedEdits_SavesWhenUserChoosesSave()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("close", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        host.Messages.ChoiceAnswer = Services.ChoiceResult.Primary;

        var cancel = vm.PagingController.Data.Single(g => g.Namespace == "buttons.cancel").Translations.Single(t => t.Language == "fr");
        cancel.Value = "Annuler";
        vm.FlushPendingEdits();
        await Task.Delay(700); // let the store's 500 ms dirty debounce settle
        await vm.CloseProjectCommand.ExecuteAsync(null);

        Assert.False(vm.HasProject);
        Assert.True(vm.ShowStartScreen);
        Assert.Equal("Annuler", Reload(host, folder)[("buttons.cancel", "fr")]);
    }

    [AvaloniaFact]
    public async Task QuickTranslate_WithMockProvider_FillsEmptyValues()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("mt", ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.AppOptions.LastProvider = "Mock";

        await vm.TranslateKeyAsync("buttons.cancel");

        var fr = vm.AllTranslation.Single(t => t.Namespace == "buttons.cancel" && t.Language == "fr");
        Assert.False(string.IsNullOrEmpty(fr.Value));
        Assert.Equal(ChangeType.Suggestion, fr.ChangeType);
        Assert.True(vm.IsDirty);
    }
}
