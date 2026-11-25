using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

public sealed class SourceAdapterRegistrationTests
{
    private sealed class StubAdapter(SourceAdapterDescriptor descriptor) : IContentSourceAdapter
    {
        public SourceAdapterDescriptor Descriptor { get; } = descriptor;
        public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class DiscoveringAdapter(SourceAdapterDescriptor descriptor) : IContentSourceAdapter, ISupportsProjectDiscovery
    {
        public SourceAdapterDescriptor Descriptor { get; } = descriptor;
        public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<RemoteProjectSummary> ListProjectsAsync(ISourceAccountContext account, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static PluginManifest Manifest(bool userDefinedHosts = false, string[]? capabilities = null) => new()
    {
        Id = "acme.lokalise", Name = "Lokalise", Version = "1.0.0", ApiVersion = "1.3", EntryAssembly = "a.dll",
        Capabilities = capabilities ?? ["sources"],
        Network = new NetworkAccess { Hosts = ["api.lokalise.com", "app.lokalise.com"], UserDefinedHosts = userDefinedHosts },
    };

    private static SourceAdapterDescriptor Descriptor(string id = "acme.lokalise.api") => new()
    {
        Id = id,
        DisplayName = "Lokalise",
        DefaultBaseUrl = new Uri("https://api.lokalise.com/api2/"),
        AuthMethods =
        [
            new ApiTokenAuth("token", "API token"),
            new OAuthAuthorizationCodeAuth("oauth", "Sign in with Lokalise", new Uri("https://app.lokalise.com/oauth2/auth"),
                new Uri("https://app.lokalise.com/oauth2/token"), "client-id", ["read_projects", "write_keys"]),
        ],
        Capabilities = SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.ConditionalWrites,
    };

    private static PluginContext Context(PluginManifest? manifest = null, ReservedIds? reserved = null) =>
        new(manifest ?? Manifest(), ".", NullLogger.Instance, reserved ?? new ReservedIds());

    [Fact]
    public void AValidAdapterIsRecordedAndSummarized()
    {
        var context = Context();
        context.AddSourceAdapter(new StubAdapter(Descriptor()));

        Assert.Single(context.SourceAdapters);
        Assert.Contains("source:acme.lokalise.api", context.Summary);
    }

    [Fact]
    public void TheSourcesCapabilityIsRequired()
    {
        var context = Context(Manifest(capabilities: ["commands"]));
        var error = Assert.Throws<PluginRegistrationException>(() => context.AddSourceAdapter(new StubAdapter(Descriptor())));
        Assert.Contains("'sources' capability", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, Func<SourceAdapterDescriptor, SourceAdapterDescriptor>> BrokenDescriptors => new()
    {
        { "must start with its plugin ID", d => d with { Id = "other.lokalise" } },
        { "is invalid", d => d with { Id = "acme.lokalise.API" } },
        { "needs a display name", d => d with { DisplayName = " " } },
        { "at least one sign-in method", d => d with { AuthMethods = [] } },
        { "empty or repeated ID", d => d with { AuthMethods = [new ApiTokenAuth("t", "A"), new BasicAuth("t", "B")] } },
        { "ReadUnits or ReadDocuments", d => d with { Capabilities = SourceCapabilities.None } },
        { "unit writes without ReadUnits", d => d with { Capabilities = SourceCapabilities.ReadDocuments | SourceCapabilities.WriteValues } },
        { "WriteDocuments without ReadDocuments", d => d with { Capabilities = SourceCapabilities.ReadUnits | SourceCapabilities.WriteDocuments } },
        { "does not implement ISupportsProjectDiscovery", d => d with { Capabilities = SourceCapabilities.ReadUnits | SourceCapabilities.ProjectDiscovery } },
        { "is not in the manifest's 'network' hosts", d => d with { DefaultBaseUrl = new Uri("https://api.evil.example/") } },
        { "must use HTTPS", d => d with { DefaultBaseUrl = new Uri("http://api.lokalise.com/") } },
        { "needs \"userDefinedHosts\": true", d => d with { AllowsCustomBaseUrl = true } },
        { "needs a DefaultBaseUrl", d => d with { DefaultBaseUrl = null } },
        { "contract 2", d => d with { ContractVersion = 2 } },
        { "must implement ISourceAuthExchange", d => d with { AuthMethods = [new CustomAuth("login", "Log in", new ConfigSchema { Fields = [new ConfigField { Key = "user", Label = "User" }] })] } },
        { "the token address host 'auth.evil.example'", d => d with { AuthMethods = [new OAuthDeviceCodeAuth("device", "Device", new Uri("https://app.lokalise.com/device"), new Uri("https://auth.evil.example/token"), "id", [])] } },
    };

    [Theory]
    [MemberData(nameof(BrokenDescriptors))]
    public void BrokenDescriptorsAreRefusedWithAReason(string expected, Func<SourceAdapterDescriptor, SourceAdapterDescriptor> change)
    {
        var error = Assert.Throws<PluginRegistrationException>(() => Context().AddSourceAdapter(new StubAdapter(change(Descriptor()))));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoopbackAddressesNeedNoHostEntry()
    {
        var context = Context();
        context.AddSourceAdapter(new StubAdapter(Descriptor() with { DefaultBaseUrl = new Uri("http://127.0.0.1:5005/") }));
        Assert.Single(context.SourceAdapters);
    }

    [Fact]
    public void CustomAddressesAreAllowedWhenTheManifestSaysSo()
    {
        var context = Context(Manifest(userDefinedHosts: true));
        context.AddSourceAdapter(new DiscoveringAdapter(Descriptor() with
        {
            DefaultBaseUrl = null, AllowsCustomBaseUrl = true,
            Capabilities = SourceCapabilities.ReadUnits | SourceCapabilities.ProjectDiscovery,
        }));
        Assert.Single(context.SourceAdapters);
    }

    [Fact]
    public void AnAdapterIdCanBeClaimedOnlyOnce()
    {
        var reserved = new ReservedIds();
        var first = Context(reserved: reserved);
        first.AddSourceAdapter(new StubAdapter(Descriptor()));
        Assert.Throws<PluginRegistrationException>(() => first.AddSourceAdapter(new StubAdapter(Descriptor())));
        first.Commit();

        var error = Assert.Throws<PluginRegistrationException>(() => Context(reserved: reserved).AddSourceAdapter(new StubAdapter(Descriptor())));
        Assert.Contains("already provided", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCatalogListsBuiltInAndPluginAdaptersAndRejectsDuplicates()
    {
        var builtIn = new StubAdapter(Descriptor("toucan.sources.local") with { DisplayName = "Local folder", DefaultBaseUrl = new Uri("http://localhost/") });
        var services = new ServiceCollection().AddBuiltInSourceAdapter(builtIn);
        services.AddSingleton(new RegisteredSourceAdapter("acme.lokalise", new StubAdapter(Descriptor())));
        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<ISourceAdapterCatalog>();

        Assert.Equal(["Local folder", "Lokalise"], catalog.Adapters.Select(a => a.Descriptor.DisplayName));
        Assert.True(catalog.Find("TOUCAN.sources.local")!.IsBuiltIn);
        Assert.Equal("acme.lokalise", catalog.Find("acme.lokalise.api")!.PluginId);
        Assert.Null(catalog.Find("acme.missing"));

        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddBuiltInSourceAdapter(new StubAdapter(Descriptor())));
    }

    [Fact]
    public void AddToucanCoreRegistersAnEmptyCatalog()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetRequiredService<ISourceAdapterCatalog>().Adapters);
    }
}
