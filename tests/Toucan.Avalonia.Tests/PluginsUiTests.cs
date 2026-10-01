using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class PluginsUiTests : IDisposable
{
    private const string Hash = "8a2d9b1f0700b11098685a79cc6054541e20d6b302295fc3e4cc3946bb366b1c";

    private readonly string _dir = Directory.CreateTempSubdirectory("toucan-plugins-ui-").FullName;
    private readonly FakeMessageService _messages = new();
    private readonly FilePluginPolicyStore _policy;

    public PluginsUiTests() => _policy = new FilePluginPolicyStore(Path.Combine(_dir, "policy.json"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PluginManifest Manifest(string id, string name = "Acme Formats") =>
        new() { Id = id, Name = name, Version = "1.2.0", ApiVersion = "1.0", EntryAssembly = "a.dll", Author = "Acme", Description = "Adds formats" };

    private PluginLoadResult Result(string id, PluginStatus status, PluginTrustState? trust = null, string? error = null) =>
        new(Path.Combine(_dir, id), status, Manifest(id), error, ContentHash: Hash, Trust: trust,
            Registered: status == PluginStatus.Loaded ? ["format:acme"] : null);

    private sealed class Catalog(params PluginLoadResult[] plugins) : IPluginCatalog
    {
        public IReadOnlyList<PluginLoadResult> Plugins { get; } = plugins;
    }

    private OptionsViewModel Options(params PluginLoadResult[] plugins)
    {
        var host = new TestHost();
        var prefs = host.Services.GetRequiredService<IPreferenceService>();
        var defaults = host.Services.GetRequiredService<IProjectDefaultsService>();
        return new OptionsViewModel(prefs, defaults, host.Dialogs, _messages, pluginCatalog: new Catalog(plugins), pluginPolicy: _policy);
    }

    // --- startup prompt ---------------------------------------------------------------------------------------

    [Fact]
    public void OnlyUntrustedPluginsWithReadableContentArePending()
    {
        var catalog = new Catalog(
            Result("a", PluginStatus.NeedsTrust, PluginTrustState.Untrusted),
            Result("b", PluginStatus.Loaded, PluginTrustState.Trusted),
            Result("c", PluginStatus.Disabled),
            Result("d", PluginStatus.Failed),
            new PluginLoadResult(_dir, PluginStatus.NeedsTrust, Manifest("e")));

        Assert.Equal(["a"], PluginPrompt.Pending(catalog, _policy).Select(p => p.DisplayId));
    }

    [Fact]
    public async Task PromptDoesNothingWhenNothingIsPending()
    {
        var shown = await PluginPrompt.RunAsync(new Catalog(Result("b", PluginStatus.Loaded, PluginTrustState.Trusted)), _policy, _messages, () => Assert.Fail("opened"));

        Assert.False(shown);
        Assert.Empty(_messages.Choices);
    }

    [Fact]
    public async Task ReviewOpensThePluginsPageAndTrustsNothing()
    {
        _messages.ChoiceAnswer = ChoiceResult.Primary;
        var opened = false;
        var catalog = new Catalog(Result("a", PluginStatus.NeedsTrust, PluginTrustState.Untrusted));

        var result = await PluginPrompt.RunAsync(catalog, _policy, _messages, () => opened = true);

        Assert.True(result);
        Assert.True(opened);
        Assert.Equal(PluginTrustState.Untrusted, _policy.GetTrust("a", Hash));
        Assert.Contains("Acme Formats 1.2.0", Assert.Single(_messages.Choices));
        Assert.Contains("not trusted", _messages.Choices[0]);
    }

    [Fact]
    public async Task LaterAsksAgainNextTime()
    {
        _messages.ChoiceAnswer = ChoiceResult.Cancel;
        var catalog = new Catalog(Result("a", PluginStatus.NeedsTrust, PluginTrustState.Untrusted));

        Assert.False(await PluginPrompt.RunAsync(catalog, _policy, _messages, () => Assert.Fail("opened")));
        Assert.Single(PluginPrompt.Pending(catalog, _policy));
    }

    [Fact]
    public async Task DontAskAgainIsRememberedForThatContentOnly()
    {
        _messages.ChoiceAnswer = ChoiceResult.Secondary;
        var catalog = new Catalog(Result("a", PluginStatus.NeedsTrust, PluginTrustState.Untrusted));

        await PluginPrompt.RunAsync(catalog, _policy, _messages, () => { });

        Assert.Empty(PluginPrompt.Pending(catalog, _policy));
        var changed = new Catalog(new PluginLoadResult(_dir, PluginStatus.NeedsTrust, Manifest("a"), ContentHash: "different", Trust: PluginTrustState.Changed));
        Assert.Single(PluginPrompt.Pending(changed, _policy));
    }

    [Fact]
    public async Task PromptFlagsChangedPlugins()
    {
        _messages.ChoiceAnswer = ChoiceResult.Cancel;

        await PluginPrompt.RunAsync(new Catalog(Result("a", PluginStatus.NeedsTrust, PluginTrustState.Changed)), _policy, _messages, () => { });

        Assert.Contains("changed since you trusted it", Assert.Single(_messages.Choices));
    }

    // --- Plugins page -----------------------------------------------------------------------------------------

    [Fact]
    public void PluginsPageSitsBeforeAboutAndAboutStaysLast()
    {
        Assert.Equal("Plugins", OptionsViewModel.Pages[OptionsViewModel.PluginsPage]);
        Assert.Equal("About", OptionsViewModel.Pages[^1]);
    }

    [Fact]
    public void PageListsEveryPluginWithItsState()
    {
        var vm = Options(
            Result("loaded", PluginStatus.Loaded, PluginTrustState.Trusted),
            Result("pending", PluginStatus.NeedsTrust, PluginTrustState.Untrusted),
            Result("changed", PluginStatus.NeedsTrust, PluginTrustState.Changed),
            Result("off", PluginStatus.Disabled),
            Result("bad", PluginStatus.Failed, error: "boom"));

        Assert.True(vm.HasPlugins);
        Assert.False(vm.HasNoPlugins);
        Assert.Equal(["Loaded", "Not trusted · not loaded", "Changed since trusted · not loaded", "Disabled", "Failed to load"],
            vm.Plugins.Select(p => p.StatusText));
        Assert.Equal([true, true, true, false, true], vm.Plugins.Select(p => p.IsEnabled));
        Assert.Equal("boom", vm.Plugins[4].Note);
        Assert.Equal("Not signed", vm.Plugins[0].SignatureText);
        Assert.Equal("8a2d9b1f0700…", vm.Plugins[0].ShortHash);
        Assert.Equal("format:acme", vm.Plugins[0].ProvidesText);
    }

    [Fact]
    public void EmptyPageSaysNoPlugins()
    {
        var vm = Options();
        Assert.True(vm.HasNoPlugins);
        Assert.False(vm.PluginsChanged);
    }

    [Fact]
    public void TogglingEnabledIsSavedAndFlagsARestart()
    {
        var vm = Options(Result("a", PluginStatus.Loaded, PluginTrustState.Trusted));

        vm.Plugins[0].IsEnabled = false;

        Assert.False(_policy.IsEnabled("a"));
        Assert.True(vm.PluginsChanged);
        Assert.Equal("Disabled after restart", vm.Plugins[0].StatusText);
        Assert.False(new FilePluginPolicyStore(Path.Combine(_dir, "policy.json")).IsEnabled("a"));
    }

    [Fact]
    public async Task TrustAsksForConfirmationThenStoresTheExactHash()
    {
        var vm = Options(Result("a", PluginStatus.NeedsTrust, PluginTrustState.Untrusted));
        var item = vm.Plugins[0];
        Assert.True(item.CanTrust);
        Assert.False(item.CanRevoke);

        _messages.ConfirmAnswer = false;
        await item.TrustCommand.ExecuteAsync(null);
        Assert.Equal(PluginTrustState.Untrusted, _policy.GetTrust("a", Hash));
        Assert.False(vm.PluginsChanged);

        _messages.ConfirmAnswer = true;
        await item.TrustCommand.ExecuteAsync(null);
        Assert.Equal(PluginTrustState.Trusted, _policy.GetTrust("a", Hash));
        Assert.True(vm.PluginsChanged);
        Assert.False(item.CanTrust);
        Assert.True(item.CanRevoke);
        Assert.Equal("Trusted · loads after restart", item.StatusText);
    }

    [Fact]
    public void RevokeRemovesTrust()
    {
        _policy.Trust("a", Hash);
        var vm = Options(Result("a", PluginStatus.Loaded, PluginTrustState.Trusted));

        vm.Plugins[0].RevokeCommand.Execute(null);

        Assert.Equal(PluginTrustState.Untrusted, _policy.GetTrust("a", Hash));
        Assert.True(vm.Plugins[0].CanTrust);
        Assert.Equal("Not trusted · won't load after restart", vm.Plugins[0].StatusText);
    }

    [Fact]
    public void RejectedAndUnreadablePluginsCannotBeManaged()
    {
        var rejected = Result("bad", PluginStatus.Rejected, error: "Its signature is invalid.");
        var unreadable = new PluginLoadResult(Path.Combine(_dir, "broken"), PluginStatus.Rejected, null, "not JSON");
        var vm = Options(rejected, unreadable);

        Assert.All(vm.Plugins, p =>
        {
            Assert.False(p.CanManage);
            Assert.False(p.CanTrust);
            Assert.False(p.CanRevoke);
        });
        Assert.Equal("broken", vm.Plugins[1].Id);
        Assert.Equal("Its signature is invalid.", vm.Plugins[0].Note);
    }

    [AvaloniaFact]
    public void PluginsPageRenders()
    {
        var vm = Options(
            Result("loaded", PluginStatus.Loaded, PluginTrustState.Trusted),
            Result("pending", PluginStatus.NeedsTrust, PluginTrustState.Untrusted),
            Result("bad", PluginStatus.Failed, error: "boom"));
        vm.SelectedPageIndex = OptionsViewModel.PluginsPage;
        vm.Plugins[0].IsEnabled = false; // shows the restart banner too

        var dialog = new OptionsDialog(vm);
        dialog.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(dialog.CaptureRenderedFrame());
        dialog.Close();
    }

    [Fact]
    public void AppContainerExposesAnEmptyCatalogAndPolicy()
    {
        using var host = new TestHost();

        Assert.Empty(host.Services.GetRequiredService<IPluginCatalog>().Plugins);
        Assert.NotNull(host.Services.GetRequiredService<IPluginPolicyStore>());
        Assert.Same(host.Services.GetRequiredService<IPluginPolicyStore>(), host.Services.GetRequiredService<IPluginPolicyStore>());
    }
}
