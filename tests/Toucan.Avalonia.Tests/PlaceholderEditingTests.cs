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
}
