using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>
/// v0.24 R3: a bulk value operation undoes and redoes as a single step through the same stack as a manual edit
/// (closes APP-04 for value operations). Move-to-namespace and delete are structural and need an extended undo
/// model; they are tracked separately.
/// </summary>
public class BulkUndoTests
{
    private const string En = """{"a": "Alpha", "b": "Beta", "c": "Gamma"}""";
    private const string Fr = """{"a": "", "b": "", "c": ""}""";

    private static Dictionary<(string Ns, string Lang), string> Reload(TestHost host, string folder) =>
        host.Services.GetRequiredService<IProjectService>().LoadProject(folder).Translations
            .GroupBy(t => (t.Namespace, t.Language)).ToDictionary(g => g.Key, g => g.First().Value);

    private static string Val(MainWindowViewModel vm, string ns, string lang) =>
        vm.AllTranslation.Single(t => t.Namespace == ns && t.Language == lang).Value;

    private static async Task<(TestHost Host, MainWindowViewModel Vm, string Folder)> Open(string name)
    {
        var host = new TestHost();
        var folder = host.CreateJsonProject(name, ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        return (host, vm, folder);
    }

    [AvaloniaFact]
    public async Task CopyToLanguage_UndoesAndRedoesAsOneStep()
    {
        var (host, vm, _) = await Open("copy");
        using (host)
        {
            vm.SelectedKeys.Add("a");
            vm.SelectedKeys.Add("b");
            vm.SelectedKeys.Add("c");
            host.Dialogs.Picks.Enqueue("fr"); // copy the en values into fr
            var hadUndoBefore = vm.UndoCommand.CanExecute(null);

            await vm.BulkCopyToLanguageCommand.ExecuteAsync(null);

            Assert.Equal("Alpha", Val(vm, "a", "fr"));
            Assert.Equal("Beta", Val(vm, "b", "fr"));
            Assert.Equal("Gamma", Val(vm, "c", "fr"));
            Assert.True(vm.IsDirty);

            vm.UndoCommand.Execute(null); // one step restores all three
            Assert.Equal("", Val(vm, "a", "fr"));
            Assert.Equal("", Val(vm, "b", "fr"));
            Assert.Equal("", Val(vm, "c", "fr"));
            Assert.Equal(hadUndoBefore, vm.UndoCommand.CanExecute(null)); // the op added exactly one step

            vm.RedoCommand.Execute(null); // one step reapplies all three
            Assert.Equal("Alpha", Val(vm, "a", "fr"));
            Assert.Equal("Gamma", Val(vm, "c", "fr"));
        }
    }

    [AvaloniaFact]
    public async Task CopyToLanguage_SurvivesSaveAndReopen()
    {
        var (host, vm, folder) = await Open("copy-save");
        try
        {
            vm.SelectedKeys.Add("a");
            vm.SelectedKeys.Add("b");
            vm.SelectedKeys.Add("c");
            host.Dialogs.Picks.Enqueue("fr");
            await vm.BulkCopyToLanguageCommand.ExecuteAsync(null);
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.Equal("Alpha", Reload(host, folder)[("a", "fr")]);

            using var second = new TestHost(host.Root);
            var reopened = second.CreateViewModel();
            await reopened.OpenProjectAsync(folder);
            Assert.Equal("Alpha", Val(reopened, "a", "fr"));
        }
        finally { host.Dispose(); }
    }

    [AvaloniaFact]
    public async Task ReplaceAll_UndoesAsOneStep()
    {
        var (host, vm, _) = await Open("replace");
        using (host)
        {
            // Fill fr so there is something to replace across several values.
            vm.SelectedKeys.Add("a");
            vm.SelectedKeys.Add("b");
            vm.SelectedKeys.Add("c");
            host.Dialogs.Picks.Enqueue("fr");
            await vm.BulkCopyToLanguageCommand.ExecuteAsync(null);
            vm.SelectedKeys.Clear();

            vm.SearchQuery = "a"; // matches Alpha, Beta, Gamma
            vm.ExecuteSearchCommand.Execute(null);
            vm.ReplaceText = "A";
            await vm.ExecuteReplaceCommand.ExecuteAsync(null);

            var afterReplace = (Val(vm, "a", "en"), Val(vm, "b", "en"), Val(vm, "c", "en"));
            Assert.NotEqual(("Alpha", "Beta", "Gamma"), afterReplace); // something changed

            vm.UndoCommand.Execute(null); // one step restores every replaced value
            Assert.Equal("Alpha", Val(vm, "a", "en"));
            Assert.Equal("Beta", Val(vm, "b", "en"));
            Assert.Equal("Gamma", Val(vm, "c", "en"));

            vm.RedoCommand.Execute(null); // one step reapplies the whole replace
            Assert.Equal(afterReplace, (Val(vm, "a", "en"), Val(vm, "b", "en"), Val(vm, "c", "en")));
        }
    }

    [AvaloniaFact]
    public async Task TextTransform_UppercaseUndoesAsOneStep()
    {
        var (host, vm, _) = await Open("upper");
        using (host)
        {
            await vm.ConvertUppercaseCommand.ExecuteAsync(null); // transforms every visible en value

            Assert.Equal("ALPHA", Val(vm, "a", "en"));
            Assert.Equal("GAMMA", Val(vm, "c", "en"));

            vm.UndoCommand.Execute(null); // one step restores all transformed values
            Assert.Equal("Alpha", Val(vm, "a", "en"));
            Assert.Equal("Beta", Val(vm, "b", "en"));
            Assert.Equal("Gamma", Val(vm, "c", "en"));

            vm.RedoCommand.Execute(null); // one step reapplies the whole transform
            Assert.Equal("ALPHA", Val(vm, "a", "en"));
            Assert.Equal("BETA", Val(vm, "b", "en"));
            Assert.Equal("GAMMA", Val(vm, "c", "en"));
        }
    }
}
