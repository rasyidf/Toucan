using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>Settings → Plugins lists the modules compiled into Toucan, read-only, with their versions.</summary>
public sealed class BuiltInModulesPageTests
{
    private sealed class Catalog(params BuiltInModuleInfo[] modules) : IPluginCatalog
    {
        public IReadOnlyList<PluginLoadResult> Plugins { get; } = [];
        public IReadOnlyList<BuiltInModuleInfo> BuiltInModules { get; } = modules;
    }

    private static OptionsViewModel Options(IPluginCatalog? catalog)
    {
        var host = new TestHost();
        return new OptionsViewModel(
            host.Services.GetRequiredService<IPreferenceService>(),
            host.Services.GetRequiredService<IProjectDefaultsService>(),
            host.Dialogs, new FakeMessageService(), pluginCatalog: catalog);
    }

    [AvaloniaFact]
    public void ListsEachModuleWithItsVersionAndWhatItProvides()
    {
        var vm = Options(new Catalog(new BuiltInModuleInfo("toucan.validation", "Built-in validation rules", "Six checks", ["validation"], ["rules:6"], "0.19.0")));

        var row = Assert.Single(vm.BuiltInModules);
        Assert.Equal("toucan.validation", row.Id);
        Assert.Equal("0.19.0", row.Version);
        Assert.True(row.HasVersion);
        Assert.Equal("rules:6", row.ProvidesText);
        Assert.True(vm.HasBuiltInModules);
    }

    [AvaloniaFact]
    public void ModuleWithoutAVersionHidesTheBadge()
    {
        var row = Assert.Single(Options(new Catalog(new BuiltInModuleInfo("acme", "Acme", null, [], [], ""))).BuiltInModules);

        Assert.False(row.HasVersion);
        Assert.False(row.HasDescription);
    }

    [AvaloniaFact]
    public void TheRealCompositionListsAllSevenShippedModules()
    {
        var host = new TestHost();
        var vm = Options(host.Services.GetRequiredService<IPluginCatalog>());

        Assert.Equal(
            ["toucan.formats.data", "toucan.formats.json", "toucan.formats.text", "toucan.formats.xml", "toucan.frameworks", "toucan.providers", "toucan.validation"],
            vm.BuiltInModules.Select(m => m.Id).Order(StringComparer.Ordinal));
        Assert.All(vm.BuiltInModules, m => Assert.True(m.HasVersion));
    }

    [AvaloniaFact]
    public void WithoutACatalogThereAreNoModules()
    {
        Assert.False(Options(null).HasBuiltInModules);
    }
}
