using Avalonia.Headless.XUnit;
using Toucan.Avalonia.ViewModels;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>v0.24 R5: the inspector surfaces the placeholders a translation must keep, parsed from the source value.</summary>
public class InspectorProvenanceTests
{
    private const string En = """{"welcome": "Hi {name}, you have {{count}} messages (%s)", "plain": "Just text"}""";
    private const string Fr = """{"welcome": "", "plain": ""}""";

    private static async Task<(TestHost Host, MainWindowViewModel Vm)> Open(string name)
    {
        var host = new TestHost();
        var folder = host.CreateJsonProject(name, ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        return (host, vm);
    }

    [AvaloniaFact]
    public async Task SelectingAKeyListsItsSourcePlaceholders()
    {
        var (host, vm) = await Open("placeholders");
        using (host)
        {
            vm.SelectedGroup = vm.PagingController.Data.Single(g => g.Namespace == "welcome");

            Assert.True(vm.HasSelectedKeyPlaceholders);
            Assert.Contains("{name}", vm.SelectedKeyPlaceholders);
            Assert.Contains("{{count}}", vm.SelectedKeyPlaceholders);
            Assert.Contains("%s", vm.SelectedKeyPlaceholders);
        }
    }

    [AvaloniaFact]
    public async Task AKeyWithNoPlaceholdersShowsNone()
    {
        var (host, vm) = await Open("none");
        using (host)
        {
            vm.SelectedGroup = vm.PagingController.Data.Single(g => g.Namespace == "plain");

            Assert.False(vm.HasSelectedKeyPlaceholders);
            Assert.Empty(vm.SelectedKeyPlaceholders);
        }
    }

    [AvaloniaFact]
    public async Task SwitchingKeysReplacesThePlaceholderList()
    {
        var (host, vm) = await Open("switch");
        using (host)
        {
            vm.SelectedGroup = vm.PagingController.Data.Single(g => g.Namespace == "welcome");
            Assert.True(vm.HasSelectedKeyPlaceholders);

            vm.SelectedGroup = vm.PagingController.Data.Single(g => g.Namespace == "plain");
            Assert.Empty(vm.SelectedKeyPlaceholders); // cleared for the new key
        }
    }
}
