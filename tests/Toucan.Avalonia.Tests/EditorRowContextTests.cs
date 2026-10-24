using Avalonia.Headless.XUnit;
using Toucan.Avalonia.ViewModels;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>v0.24 R2: counts, side-by-side source, and focus-safe refresh on the editor rows.</summary>
public class EditorRowContextTests
{
    private const string En = """{"greeting": "Hello there world"}""";
    private const string Fr = """{"greeting": "Bonjour"}""";

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

    [AvaloniaFact]
    public async Task CharacterAndWordCountsReflectTheValue()
    {
        var (host, vm) = await Open("counts");
        using (host)
        {
            var en = Row(vm, "en");
            Assert.Equal(17, en.CharacterCount);     // "Hello there world"
            Assert.Equal(3, en.WordCount);
            Assert.Contains("17 chars", en.CountSummary);
            Assert.Contains("3 words", en.CountSummary);

            en.Value = "Hi";
            Assert.Equal(2, en.CharacterCount);
            Assert.Equal(1, en.WordCount);
            Assert.Contains("1 word", en.CountSummary);
        }
    }

    [AvaloniaFact]
    public async Task NonPrimaryRowCarriesThePrimaryValueAsItsSource()
    {
        var (host, vm) = await Open("source");
        using (host)
        {
            var en = Row(vm, "en");
            var fr = Row(vm, "fr");

            Assert.False(en.HasSource);          // the primary row is the source
            Assert.Null(en.SourceText);
            Assert.True(fr.HasSource);
            Assert.Equal("Hello there world", fr.SourceText);
        }
    }

    [AvaloniaFact]
    public async Task RefreshDoesNotRaiseValueWhenTheValueDidNotChange()
    {
        var (host, vm) = await Open("refresh");
        using (host)
        {
            var fr = Row(vm, "fr");
            var valueRaised = 0;
            fr.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TranslationItemViewModel.Value)) valueRaised++; };

            fr.Refresh(); // model value unchanged → must not reset the (possibly focused) field

            Assert.Equal(0, valueRaised);
        }
    }

    [AvaloniaFact]
    public async Task RefreshRaisesValueAndCountsWhenTheModelChanged()
    {
        var (host, vm) = await Open("refresh2");
        using (host)
        {
            var fr = Row(vm, "fr");
            var raised = new List<string?>();
            fr.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            fr.Model.Value = "Salut tout le monde"; // external change (e.g. undo/bulk)
            fr.Refresh();

            Assert.Contains(nameof(TranslationItemViewModel.Value), raised);
            Assert.Contains(nameof(TranslationItemViewModel.CountSummary), raised);
            Assert.Equal(4, fr.WordCount);
        }
    }
}
