using Toucan.Modules;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

public sealed class PluginTrustTests : IDisposable
{
    private const string Full = "Toucan.TestPlugins.FullPlugin";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "toucan-trust-" + Guid.NewGuid().ToString("N"));
    private readonly string _policyFile;

    public PluginTrustTests()
    {
        Directory.CreateDirectory(_root);
        _policyFile = Path.Combine(_root, "state", "plugin-policy.json");
    }

    public void Dispose()
    {
        TempFolder.TryDelete(_root);
    }

    private string Install(string folder, string id)
    {
        var dir = Path.Combine(_root, "plugins", folder);
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestPlugins")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
        File.WriteAllText(Path.Combine(dir, "plugin.json"), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id, ["name"] = id, ["version"] = "1.0.0", ["apiVersion"] = "1.0",
            ["entryAssembly"] = "Toucan.TestPlugins.dll", ["entryType"] = Full,
            ["capabilities"] = new[] { "formats", "providers", "validation", "frameworks" },
        }));
        return dir;
    }

    private ServiceProvider Build(Action<PluginHostOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();
        var options = new PluginHostOptions();
        options.Roots.Add(Path.Combine(_root, "plugins"));
        configure(options);
        services.AddToucanPlugins(options);
        return services.BuildServiceProvider();
    }

    private static bool HasFormat(ServiceProvider sp) =>
        sp.GetRequiredService<ITranslationStrategyFactory>().GetSaveStrategy("test-fmt") is not null;

    // --- hashing ---------------------------------------------------------------------------------------------

    [Fact]
    public void HashIsStableAndHexEncoded()
    {
        var dir = Install("p", "test.p");

        var first = PluginHasher.Compute(dir);

        Assert.Equal(first, PluginHasher.Compute(dir));
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void HashChangesWhenAnyFileChanges()
    {
        var dir = Install("p", "test.p");
        var before = PluginHasher.Compute(dir);

        File.AppendAllText(Path.Combine(dir, "plugin.json"), " ");
        var edited = PluginHasher.Compute(dir);
        Assert.NotEqual(before, edited);

        File.WriteAllText(Path.Combine(dir, "extra.txt"), "x");
        var added = PluginHasher.Compute(dir);
        Assert.NotEqual(edited, added);

        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.Move(Path.Combine(dir, "extra.txt"), Path.Combine(dir, "sub", "extra.txt"));
        Assert.NotEqual(added, PluginHasher.Compute(dir));
    }

    [Fact]
    public void IdenticalContentInDifferentFoldersHashesTheSame()
    {
        Assert.Equal(PluginHasher.Compute(Install("one", "test.same")), PluginHasher.Compute(Install("two", "test.same")));
    }

    // --- policy store ----------------------------------------------------------------------------------------

    [Fact]
    public void StoreStartsEmptyEnabledAndUntrusted()
    {
        var store = new FilePluginPolicyStore(_policyFile);

        Assert.True(store.IsEnabled("a"));
        Assert.Equal(PluginTrustState.Untrusted, store.GetTrust("a", "h1"));
        Assert.False(File.Exists(_policyFile));
    }

    [Fact]
    public void TrustIsPerExactContentAndReportsChanges()
    {
        var store = new FilePluginPolicyStore(_policyFile);

        store.Trust("a", "h1");
        Assert.Equal(PluginTrustState.Trusted, store.GetTrust("a", "h1"));
        Assert.Equal(PluginTrustState.Trusted, store.GetTrust("A", "H1")); // IDs and hashes compare case-insensitively
        Assert.Equal(PluginTrustState.Changed, store.GetTrust("a", "h2"));
        Assert.Equal(PluginTrustState.Untrusted, store.GetTrust("b", "h1"));

        store.Trust("a", "h2"); // re-trusting replaces the old hash
        Assert.Equal(PluginTrustState.Changed, store.GetTrust("a", "h1"));
        Assert.Equal(PluginTrustState.Trusted, store.GetTrust("a", "h2"));

        store.Revoke("a");
        Assert.Equal(PluginTrustState.Untrusted, store.GetTrust("a", "h2"));
    }

    [Fact]
    public void DecisionsSurviveARestart()
    {
        var store = new FilePluginPolicyStore(_policyFile);
        store.Trust("a", "h1");
        store.SetEnabled("b", false);

        var reloaded = new FilePluginPolicyStore(_policyFile);

        Assert.Equal(PluginTrustState.Trusted, reloaded.GetTrust("a", "h1"));
        Assert.False(reloaded.IsEnabled("b"));
        Assert.True(reloaded.IsEnabled("a"));

        reloaded.SetEnabled("b", true);
        Assert.True(new FilePluginPolicyStore(_policyFile).IsEnabled("b"));
        Assert.False(File.Exists(_policyFile + ".tmp"));
    }

    [Fact]
    public void DismissedPromptIsPerContentAndPersists()
    {
        var store = new FilePluginPolicyStore(_policyFile);
        Assert.False(store.IsPromptDismissed("a", "h1"));

        store.DismissPrompt("a", "h1");
        Assert.True(store.IsPromptDismissed("A", "H1"));
        Assert.True(new FilePluginPolicyStore(_policyFile).IsPromptDismissed("a", "h1"));
        Assert.False(store.IsPromptDismissed("a", "h2")); // a changed plugin asks again

        store.Trust("a", "h1"); // trusting clears the dismissal
        Assert.False(store.IsPromptDismissed("a", "h1"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    public void CorruptFileMeansNothingIsTrusted(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_policyFile)!);
        File.WriteAllText(_policyFile, content);

        var store = new FilePluginPolicyStore(_policyFile);

        Assert.Equal(PluginTrustState.Untrusted, store.GetTrust("a", "h1"));
        store.Trust("a", "h1"); // and it recovers on the next write
        Assert.Equal(PluginTrustState.Trusted, new FilePluginPolicyStore(_policyFile).GetTrust("a", "h1"));
    }

    // --- host gate -------------------------------------------------------------------------------------------

    [Fact]
    public void UntrustedPluginIsNotLoadedAndNoneOfItsCodeRuns()
    {
        Install("p", "test.never-loaded");
        var store = new FilePluginPolicyStore(_policyFile);

        using var sp = Build(o => o.Policy = store);

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.NeedsTrust, result.Status);
        Assert.Equal(PluginTrustState.Untrusted, result.Trust);
        Assert.Contains("not trusted", result.Error);
        Assert.Matches("^[0-9a-f]{64}$", result.ContentHash);
        Assert.Equal("test.never-loaded", result.Manifest!.Id);
        Assert.Null(result.Registered);
        Assert.False(HasFormat(sp));
        // Nothing was loaded: a plugin load context was never created for it.
        Assert.DoesNotContain(System.Runtime.Loader.AssemblyLoadContext.All, c => c.Name == "plugin:test.never-loaded");
    }

    [Fact]
    public void TrustedPluginLoads()
    {
        var dir = Install("p", "test.p");
        var store = new FilePluginPolicyStore(_policyFile);
        store.Trust("test.p", PluginHasher.Compute(dir));

        using var sp = Build(o => o.Policy = store);

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.Loaded, result.Status);
        Assert.Equal(PluginTrustState.Trusted, result.Trust);
        Assert.Equal(PluginHasher.Compute(dir), result.ContentHash);
        Assert.True(HasFormat(sp));
    }

    [Fact]
    public void ChangedPluginNeedsTrustAgain()
    {
        var dir = Install("p", "test.p");
        var store = new FilePluginPolicyStore(_policyFile);
        store.Trust("test.p", PluginHasher.Compute(dir));
        File.WriteAllText(Path.Combine(dir, "payload.txt"), "tampered");

        using var sp = Build(o => o.Policy = store);

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.NeedsTrust, result.Status);
        Assert.Equal(PluginTrustState.Changed, result.Trust);
        Assert.Contains("changed since you trusted", result.Error);
        Assert.False(HasFormat(sp));
    }

    [Fact]
    public void RetrustingTheNewContentLoadsIt()
    {
        var dir = Install("p", "test.p");
        var store = new FilePluginPolicyStore(_policyFile);
        store.Trust("test.p", PluginHasher.Compute(dir));
        File.WriteAllText(Path.Combine(dir, "payload.txt"), "update");
        store.Trust("test.p", PluginHasher.Compute(dir));

        using var sp = Build(o => o.Policy = store);

        Assert.Equal(PluginStatus.Loaded, Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins).Status);
    }

    [Fact]
    public void DisabledByPolicyBeatsTrust()
    {
        var dir = Install("p", "test.p");
        var store = new FilePluginPolicyStore(_policyFile);
        store.Trust("test.p", PluginHasher.Compute(dir));
        store.SetEnabled("test.p", false);

        using var sp = Build(o => o.Policy = store);

        Assert.Equal(PluginStatus.Disabled, Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins).Status);
        Assert.False(HasFormat(sp));
    }

    [Fact]
    public void DisabledUntrustedPluginDoesNotAskForTrust()
    {
        Install("p", "test.p");
        var store = new FilePluginPolicyStore(_policyFile);
        store.SetEnabled("test.p", false);

        using var sp = Build(o => o.Policy = store);

        Assert.Equal(PluginStatus.Disabled, Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins).Status);
    }

    [Fact]
    public void AllowForThisRunWaivesTrustWithoutStoringIt()
    {
        Install("p", "test.p");
        var store = new FilePluginPolicyStore(_policyFile);

        using var sp = Build(o =>
        {
            o.Policy = store;
            o.AllowForThisRun.Add("TEST.P");
        });

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.Loaded, result.Status);
        Assert.Equal(PluginTrustState.Untrusted, result.Trust);
        Assert.True(HasFormat(sp));
        Assert.False(File.Exists(_policyFile), "a one-run allowance must not be persisted");
    }

    [Fact]
    public void AllowForThisRunDoesNotOverrideDisabled()
    {
        Install("p", "test.p");
        var store = new FilePluginPolicyStore(_policyFile);
        store.SetEnabled("test.p", false);

        using var sp = Build(o =>
        {
            o.Policy = store;
            o.AllowForThisRun.Add("test.p");
        });

        Assert.Equal(PluginStatus.Disabled, Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins).Status);
    }

    [Fact]
    public void WithoutAPolicyEverythingLoadsAsBefore()
    {
        Install("p", "test.p");

        using var sp = Build(_ => { });

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.Loaded, result.Status);
        Assert.Null(result.Trust);
    }

    [Fact]
    public void OnlyTheTrustedPluginOfSeveralLoads()
    {
        var a = Install("a", "test.a");
        Install("b", "test.b");
        var store = new FilePluginPolicyStore(_policyFile);
        store.Trust("test.a", PluginHasher.Compute(a));

        using var sp = Build(o => o.Policy = store);

        Assert.Equal([PluginStatus.Loaded, PluginStatus.NeedsTrust],
            sp.GetRequiredService<IPluginCatalog>().Plugins.Select(p => p.Status));
    }

    // --- signatures ------------------------------------------------------------------------------------------

    [Fact]
    public void UnsignedPluginsLoadAndAreLabelledNotSigned()
    {
        Install("p", "test.p");

        using var sp = Build(_ => { });

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.Loaded, result.Status);
        Assert.Equal(PluginSignatureStatus.NotSigned, result.Signature);
    }

    [Fact]
    public void StubVerifierNeverBlocks()
    {
        var manifest = new PluginManifest { Id = "a" };
        Assert.Equal(PluginSignatureStatus.NotSigned, new UnsignedPluginSignatureVerifier().Verify(manifest, _root));
    }

    [Fact]
    public void InvalidSignatureIsRejectedBeforeAnyCodeRuns()
    {
        Install("p", "test.p");

        using var sp = Build(o => o.SignatureVerifier = new FakeVerifier(PluginSignatureStatus.Invalid));

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.Rejected, result.Status);
        Assert.Contains("signature is invalid", result.Error);
        Assert.Equal(PluginSignatureStatus.Invalid, result.Signature);
        Assert.False(HasFormat(sp));
    }

    [Fact]
    public void ValidSignatureDoesNotReplaceTrust()
    {
        Install("p", "test.p");
        var store = new FilePluginPolicyStore(_policyFile);

        using var sp = Build(o =>
        {
            o.Policy = store;
            o.SignatureVerifier = new FakeVerifier(PluginSignatureStatus.Valid);
        });

        var result = Assert.Single(sp.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.Equal(PluginStatus.NeedsTrust, result.Status);
        Assert.Equal(PluginSignatureStatus.Valid, result.Signature);
    }

    private sealed class FakeVerifier(PluginSignatureStatus status) : IPluginSignatureVerifier
    {
        public PluginSignatureStatus Verify(PluginManifest manifest, string pluginDirectory) => status;
    }
}
