using Avalonia.Headless.XUnit;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>
/// Editor placeholder features (scoped to {{ }}): an inline warning when a target drops a source placeholder (toggleable),
/// and a gesture that renames a {{token}} across every language of a key as one undo step.
/// </summary>
public class PlaceholderEditingTests
{
    private const string En = """{"greeting": "Hi {{name}}, you have {{count}} items"}""";
    private const string Fr = """{"greeting": "Bonjour {{name}}"}""";

    private static async Task<(TestHost Host, MainWindowViewModel Vm)> Open(string name)
    {
        var host = new TestHost();
        var folder = host.CreateJsonProject(name, ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        return (host, vm);
    }

    private static TranslationItemViewModel Row(MainWindowViewModel vm, string lang) =>
        vm.PagingController.Data.Single(g => g.Namespace == "greeting").Translations.Single(t => t.Language == lang);

    // ── inline warning ──

    [AvaloniaFact]
    public async Task TargetMissingASourcePlaceholderIsFlagged()
    {
        PanelService.Instance.ShowPlaceholderWarnings = true;
        var (host, vm) = await Open("warn");
        using (host)
        {
            var fr = Row(vm, "fr"); // "Bonjour {{name}}" — missing {{count}}
            Assert.Contains("{{count}}", fr.MissingPlaceholders);
            Assert.DoesNotContain("{{name}}", fr.MissingPlaceholders);
            Assert.True(fr.HasPlaceholderWarning);
            Assert.Contains("{{count}}", fr.PlaceholderWarning);

            // The source row never warns about itself.
            Assert.False(Row(vm, "en").HasPlaceholderWarning);
        }
    }

    [AvaloniaFact]
    public async Task AddingTheMissingPlaceholderClearsTheWarning()
    {
        PanelService.Instance.ShowPlaceholderWarnings = true;
        var (host, vm) = await Open("warn-clear");
        using (host)
        {
            var fr = Row(vm, "fr");
            Assert.True(fr.HasPlaceholderWarning);

            fr.Value = "Bonjour {{name}}, vous avez {{count}} éléments";
            Assert.Empty(fr.MissingPlaceholders);
            Assert.False(fr.HasPlaceholderWarning);
        }
    }

    [AvaloniaFact]
    public async Task TheWarningIsToggleable()
    {
        var (host, vm) = await Open("warn-toggle");
        using (host)
        {
            var fr = Row(vm, "fr");

            PanelService.Instance.ShowPlaceholderWarnings = false;
            fr.RefreshPlaceholderWarning();
            Assert.NotEmpty(fr.MissingPlaceholders);     // the fact is still computed
            Assert.False(fr.HasPlaceholderWarning);      // but the warning is hidden

            PanelService.Instance.ShowPlaceholderWarnings = true;
            fr.RefreshPlaceholderWarning();
            Assert.True(fr.HasPlaceholderWarning);
        }
    }

    // ── linked rename ──

    [AvaloniaFact]
    public async Task RenameUndoAndRedoKeepWarningsInSyncWithTheSource()
    {
        var (host, vm) = await Open("rename-context");
        using (host)
        {
            var target = Row(vm, "fr");
            vm.RenamePlaceholderAcrossLanguages("greeting", "{{name}}", "{{username}}");
            Assert.Contains("{{username}}", target.SourceText);
            Assert.DoesNotContain("{{name}}", target.MissingPlaceholders);
            Assert.Contains("{{count}}", target.MissingPlaceholders);

            vm.UndoCommand.Execute(null);
            Assert.Contains("{{name}}", target.SourceText);
            Assert.DoesNotContain("{{name}}", target.MissingPlaceholders);

            vm.RedoCommand.Execute(null);
            Assert.Contains("{{username}}", target.SourceText);
            Assert.DoesNotContain("{{name}}", target.MissingPlaceholders);
        }
    }

    [AvaloniaFact]
    public async Task EditingTheSourceUpdatesTargetWarningsBeforeSave()
    {
        var (host, vm) = await Open("edit-source-context");
        using (host)
        {
            var target = Row(vm, "fr");
            Row(vm, "en").Value = "Hello {{city}}";
            Assert.Equal("Hello {{city}}", target.SourceText);
            Assert.Equal(["{{city}}"], target.MissingPlaceholders);
            vm.FlushPendingEdits();
            vm.UndoCommand.Execute(null);
            Assert.Equal("Hi {{name}}, you have {{count}} items", target.SourceText);
            Assert.Equal(["{{count}}"], target.MissingPlaceholders);
        }
    }

    [AvaloniaFact]
    public void PluralVariantsKeepIndependentLiveSourceContext()
    {
        using var group = new LanguageGroupViewModel("items", primaryLanguage: "en");
        group.LoadPluralVariants(new[]
        {
            new Toucan.Core.Models.TranslationItem { Namespace = "items_one", Language = "en", Value = "One {{name}}" },
            new Toucan.Core.Models.TranslationItem { Namespace = "items_one", Language = "fr", Value = "Un {{name}}" },
            new Toucan.Core.Models.TranslationItem { Namespace = "items_other", Language = "en", Value = "Many {{count}}" },
            new Toucan.Core.Models.TranslationItem { Namespace = "items_other", Language = "fr", Value = "Plusieurs {{count}}" }
        }.GroupBy(i => i.Namespace));
        var one = group.PluralVariants.Single(v => v.Category == "one");
        var other = group.PluralVariants.Single(v => v.Category == "other");
        one.Translations.Single(i => i.Language == "en").Value = "One {{person}}";
        Assert.Equal(["{{person}}"], one.Translations.Single(i => i.Language == "fr").MissingPlaceholders);
        Assert.Empty(other.Translations.Single(i => i.Language == "fr").MissingPlaceholders);
        Assert.Equal("Many {{count}}", other.Translations.Single(i => i.Language == "fr").SourceText);
    }

    [AvaloniaFact]
    public async Task RenamingAPlaceholderUpdatesEveryLanguageInOneUndoStep()
    {
        var (host, vm) = await Open("rename");
        using (host)
        {
            var changed = vm.RenamePlaceholderAcrossLanguages("greeting", "{{name}}", "{{username}}");

            Assert.Equal(2, changed); // en and fr both had {{name}}
            Assert.Contains("{{username}}", Row(vm, "en").Value);
            Assert.Contains("{{username}}", Row(vm, "fr").Value);
            Assert.DoesNotContain("{{name}}", Row(vm, "en").Value);
            Assert.Contains("{{count}}", Row(vm, "en").Value); // other tokens untouched

            vm.UndoCommand.Execute(null); // one step restores both
            Assert.Contains("{{name}}", Row(vm, "en").Value);
            Assert.Contains("{{name}}", Row(vm, "fr").Value);
            Assert.DoesNotContain("{{username}}", Row(vm, "en").Value);
        }
    }

    [AvaloniaFact]
    public async Task RenameRefusesMalformedOrNonMatchingTokens()
    {
        var (host, vm) = await Open("rename-guard");
        using (host)
        {
            Assert.Equal(0, vm.RenamePlaceholderAcrossLanguages("greeting", "name", "{{username}}"));     // old not a {{ }} token
            Assert.Equal(0, vm.RenamePlaceholderAcrossLanguages("greeting", "{{name}}", "username"));     // new not a {{ }} token
            Assert.Equal(0, vm.RenamePlaceholderAcrossLanguages("greeting", "{{missing}}", "{{x}}"));     // token not present
            Assert.Contains("{{name}}", Row(vm, "en").Value); // nothing changed
        }
    }

    [AvaloniaFact]
    public async Task PromptBasedRenameFromTheContextMenuRenamesAcrossLanguages()
    {
        var (host, vm) = await Open("rename-prompt");
        using (host)
        {
            host.Dialogs.Prompts.Enqueue("username"); // the user types the new inner name in the prompt

            await vm.RenamePlaceholderCommand.ExecuteAsync("greeting\t{{name}}");

            Assert.Contains("{{username}}", Row(vm, "en").Value);
            Assert.Contains("{{username}}", Row(vm, "fr").Value);
            Assert.DoesNotContain("{{name}}", Row(vm, "fr").Value);
        }
    }
}
