using Toucan.Modules;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

/// <summary>Installs the real samples/Toucan.Sample.Plugin build output into a plugin folder and drives it through Toucan.</summary>
public sealed class SamplePluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toucan-sample-" + Guid.NewGuid().ToString("N"));

    public SamplePluginTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string InstallSample()
    {
        var dir = Path.Combine(_root, "plugins", "sample.tsv");
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SamplePlugin")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        return dir;
    }

    private ServiceProvider Build(IPluginPolicy? policy = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        var options = new PluginHostOptions { Policy = policy };
        options.Roots.Add(Path.Combine(_root, "plugins"));
        services.AddToucanPlugins(options);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    [Fact]
    public void ShippedManifestIsValidAndPointsAtTheBuiltAssembly()
    {
        var dir = InstallSample();

        Assert.True(PluginManifest.TryParse(File.ReadAllText(Path.Combine(dir, "plugin.json")), out var manifest, out var errors), string.Join("; ", errors));
        Assert.Equal("sample.tsv", manifest.Id);
        Assert.True(File.Exists(Path.Combine(dir, manifest.EntryAssembly)));
        Assert.True(PluginApi.IsCompatible(manifest.ParsedApiVersion!));
        Assert.DoesNotContain("Toucan.Plugins.Abstractions.dll", Directory.GetFiles(dir).Select(Path.GetFileName));
    }

    [Fact]
    public void LoadsAndRegistersItsFormatAndRule()
    {
        InstallSample();

        using var sp = Build();

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.Loaded, result.Status);
        Assert.Equal(["format:sample-tsv", "rule:sample.todo-marker"], result.Registered);
    }

    [Fact]
    public void LoadsOnlyOnceTheUserHasTrustedIt()
    {
        var dir = InstallSample();
        var store = new FilePluginPolicyStore(Path.Combine(_root, "policy.json"));

        using (var untrusted = Build(store))
            Assert.Equal(PluginStatus.NeedsTrust, Assert.Single(untrusted.GetRequiredService<IPluginCatalog>().Plugins).Status);

        store.Trust("sample.tsv", PluginHasher.Compute(dir));
        using var trusted = Build(store);
        Assert.Equal(PluginStatus.Loaded, Assert.Single(trusted.GetRequiredService<IPluginCatalog>().Plugins).Status);
    }

    [Fact]
    public void ProjectRoundTripsThroughTheFormatIncludingAwkwardValues()
    {
        InstallSample();
        var project = Path.Combine(_root, "project");
        var awkward = "tab\there, newline\nthere, return\rand backslash \\ and a trailing slash \\";

        using var sp = Build();
        var service = sp.GetRequiredService<IProjectService>();
        var settings = service.CreateProject(project, ["en", "fr"], "sample-tsv");
        service.Save(settings, [], [
            new TranslationItem { Language = "en", Namespace = "app.title", Value = "Hello" },
            new TranslationItem { Language = "en", Namespace = "app.awkward", Value = awkward },
            new TranslationItem { Language = "fr", Namespace = "app.title", Value = "Bonjour" },
        ]);

        Assert.Equal("app.awkward\ttab\\there, newline\\nthere, return\\rand backslash \\\\ and a trailing slash \\\\", File.ReadLines(Path.Combine(project, "en.tsv")).First());
        var reopened = service.LoadProject(project);

        Assert.Equal("sample-tsv", reopened.Settings.SaveFormat);
        Assert.Equal(awkward, reopened.Translations.Single(t => t is { Language: "en", Namespace: "app.awkward" }).Value);
        Assert.Equal("Bonjour", reopened.Translations.Single(t => t is { Language: "fr", Namespace: "app.title" }).Value);
    }

    [Fact]
    public void DetectsFoldersOfTsvFilesAndImportsByExtension()
    {
        InstallSample();
        var folder = Path.Combine(_root, "detect");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "en.tsv"), "a\tb\n");

        using var sp = Build();

        Assert.Equal("sample-tsv", sp.GetRequiredService<FormatDetector>().Detect(folder));
        var strategy = sp.GetRequiredService<ITranslationStrategyFactory>().SaveStrategies.Single(s => s.FormatId == "sample-tsv");
        Assert.Contains(".tsv", strategy.FileExtensions);
        Assert.Equal("Tab-separated values (sample)", strategy.DisplayName);
    }

    [Fact]
    public void LoaderIgnoresBlankAndKeylessLines()
    {
        InstallSample();
        var folder = Path.Combine(_root, "messy");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "en.tsv"), "\n\t no key\nno tab here\napp.ok\tfine\\\n");

        using var sp = Build();
        var items = sp.GetRequiredService<ITranslationStrategyFactory>().GetLoadStrategy("sample-tsv")!.Load(folder).ToList();

        Assert.Equal("app.ok", Assert.Single(items).Namespace);
        Assert.Equal("fine\\", items[0].Value); // a lone trailing backslash is kept literally
    }

    [Theory]
    [InlineData("TODO: translate", true)]
    [InlineData("needs a fixme here", true)]
    [InlineData("All good", false)]
    [InlineData("", false)]
    public void RuleFlagsUnfinishedValues(string value, bool flagged)
    {
        InstallSample();
        using var sp = Build();

        var results = sp.GetRequiredService<IValidationPipeline>().Run(
            new ValidationContext { Items = [new TranslationItem { Language = "fr", Namespace = "k", Value = value }] },
            ["sample.todo-marker"]).ToList();

        Assert.Equal(flagged, results.Count == 1);
        if (flagged)
        {
            Assert.Equal(ValidationSeverity.Warning, results[0].Severity);
            Assert.Equal("k", results[0].Namespace);
            Assert.Equal("fr", results[0].Language);
        }
    }
}
