using System.Globalization;
using Toucan.Core.Commands;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Commands;

public class CommandRegistryTests
{
    private sealed class Handler : ICommandHandler
    {
        public CommandState State { get; set; } = CommandState.Available;
        public Func<ICommandInvocation, CancellationToken, Task>? Run { get; set; }
        public int Runs { get; private set; }
        public CommandState GetState(ICommandContext context) => State;

        public async Task ExecuteAsync(ICommandInvocation invocation, CancellationToken cancellationToken)
        {
            Runs++;
            if (Run is not null) await Run(invocation, cancellationToken);
        }
    }

    private static CommandDefinition Def(string id, string? shortcut = null, bool requiresWorkspace = false) =>
        new() { Id = id, Title = id, Category = "Test", DefaultShortcut = shortcut, RequiresWorkspace = requiresWorkspace };

    private static CommandRegistry Create(IPluginActivationService? activation = null) => new(activation, isMac: false);

    [Theory]
    [InlineData("mod+shift+k", "Mod+Shift+K")]
    [InlineData("Shift + Mod + k", "Mod+Shift+K")]
    [InlineData("control+f5", "Ctrl+f5")]
    [InlineData("F5", "F5")]
    public void ShortcutsNormalize(string input, string expected)
    {
        Assert.True(ShortcutText.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mod+")]
    [InlineData("Mod+Mod+K")]
    [InlineData("K+J")]
    [InlineData("Shift")]
    public void MalformedShortcutsAreRejected(string input) => Assert.False(ShortcutText.TryNormalize(input, out _));

    [Fact]
    public void ModResolvesPerPlatform()
    {
        Assert.Equal("Ctrl+K", ShortcutText.Resolve("Mod+K", isMac: false));
        Assert.Equal("Meta+K", ShortcutText.Resolve("Mod+K", isMac: true));
    }

    [Theory]
    [InlineData("Bad Id")]
    [InlineData("")]
    [InlineData("-x")]
    public void InvalidIdsAreRejected(string id) =>
        Assert.Throws<PluginRegistrationException>(() => Create().Register(Def(id), new Handler()));

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        var registry = Create();
        registry.Register(Def("toucan.a"), new Handler());
        Assert.Throws<PluginRegistrationException>(() => registry.Register(Def("toucan.a"), new Handler()));
    }

    [Fact]
    public void PluginCommandsNeedTheirPluginPrefixAndCannotUseTheBuiltInNamespace()
    {
        var registry = Create();
        Assert.Throws<PluginRegistrationException>(() => registry.Register(Def("other.x"), new Handler(), "acme.sync"));
        Assert.Throws<PluginRegistrationException>(() => registry.Register(Def("toucan.x.y"), new Handler(), "toucan.x"));
        registry.Register(Def("acme.sync.pull"), new Handler(), "acme.sync");
        Assert.Equal("acme.sync", registry.Find("acme.sync.pull")!.PluginId);
    }

    [Fact]
    public void PluginRegistrationsFromTheContainerAreAdded_AndBadOnesSkipped()
    {
        var good = new PluginCommandRegistration("acme.sync", Def("acme.sync.pull"), new Handler());
        var bad = new PluginCommandRegistration("acme.sync", Def("elsewhere.x"), new Handler());
        var registry = new CommandRegistry(null, [good, bad], isMac: false);

        Assert.Equal(["acme.sync.pull"], registry.Commands.Select(c => c.Id));
    }

    [Fact]
    public async Task WorkspaceCommandsAreUnavailableUntilAWorkspaceOpensAndAfterItCloses()
    {
        await using var activation = new PluginActivationService([]);
        using var registry = Create(activation);
        var handler = new Handler();
        registry.Register(Def("toucan.sync", requiresWorkspace: true), handler);

        var closed = registry.GetState("toucan.sync");
        Assert.Equal(CommandAvailability.Unavailable, closed.Availability);
        Assert.Equal(CommandRunStatus.Refused, (await registry.ExecuteAsync("toucan.sync")).Status);
        Assert.Equal(0, handler.Runs);

        await activation.OpenWorkspaceAsync("w");
        Assert.True(registry.GetState("toucan.sync").CanRun);
        Assert.Equal(CommandRunStatus.Completed, (await registry.ExecuteAsync("toucan.sync")).Status);

        await activation.CloseWorkspaceAsync("w");
        Assert.False(registry.GetState("toucan.sync").CanRun);
    }

    [Fact]
    public async Task ClosingTheWorkspaceCancelsARunningCommand()
    {
        await using var activation = new PluginActivationService([]);
        using var registry = Create(activation);
        var started = new TaskCompletionSource();
        registry.Register(Def("toucan.long", requiresWorkspace: true), new Handler
        {
            Run = async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
        });
        await activation.OpenWorkspaceAsync("w");

        var running = registry.ExecuteAsync("toucan.long");
        await started.Task;
        Assert.Single(registry.ActiveRuns);
        await activation.CloseWorkspaceAsync("w");

        Assert.Equal(CommandRunStatus.Cancelled, (await running).Status);
        Assert.Empty(registry.ActiveRuns);
    }

    [Theory]
    [InlineData(CommandAvailability.Disconnected)]
    [InlineData(CommandAvailability.Unlicensed)]
    [InlineData(CommandAvailability.Hidden)]
    public async Task HandlerStatesStopExecutionAndAreReported(CommandAvailability availability)
    {
        var registry = Create();
        var handler = new Handler { State = new CommandState(availability, "because") };
        registry.Register(Def("toucan.x"), handler);

        var run = await registry.ExecuteAsync("toucan.x");

        Assert.Equal(CommandRunStatus.Refused, run.Status);
        Assert.Equal(availability, run.RefusedBecause!.Value.Availability);
        Assert.Equal(0, handler.Runs);
    }

    [Fact]
    public async Task HandlerExceptionsFailTheRunOnly()
    {
        var registry = Create();
        registry.Register(Def("toucan.x"), new Handler { Run = (_, _) => throw new InvalidOperationException("boom") });

        var run = await registry.ExecuteAsync("toucan.x");

        Assert.Equal(CommandRunStatus.Failed, run.Status);
        Assert.Contains("boom", run.Error);
    }

    [Fact]
    public async Task ProgressAndCompletionAreRaised()
    {
        var registry = Create();
        var seen = new List<CommandRunStatus>();
        var messages = new List<string?>();
        registry.RunChanged += (_, e) => { seen.Add(e.Run.Status); messages.Add(e.Run.Progress?.Message); };
        registry.Register(Def("toucan.x"), new Handler
        {
            Run = async (inv, ct) =>
            {
                inv.Progress.Report(new CommandProgressInfo("half", 0.5));
                await Task.Delay(50, ct);
            },
        });

        await registry.ExecuteAsync("toucan.x", parameter: "p");

        Assert.Equal(CommandRunStatus.Running, seen[0]);
        Assert.Equal(CommandRunStatus.Completed, seen[^1]);
        Assert.Contains("half", messages);
    }

    [Fact]
    public async Task ParameterIsPassedToTheHandler()
    {
        var registry = Create();
        object? received = null;
        registry.Register(Def("toucan.x"), new Handler { Run = (inv, _) => { received = inv.Parameter; return Task.CompletedTask; } });

        await registry.ExecuteAsync("toucan.x", 42);

        Assert.Equal(42, received);
    }

    [Fact]
    public void DefaultShortcutsOverridesAndReset()
    {
        var registry = Create();
        registry.Register(Def("toucan.a", "Mod+K"), new Handler());
        Assert.Equal("Mod+K", registry.GetShortcut("toucan.a"));

        Assert.True(registry.TrySetShortcut("toucan.a", "mod+shift+j", out _));
        Assert.Equal("Mod+Shift+J", registry.GetShortcut("toucan.a"));

        Assert.True(registry.TrySetShortcut("toucan.a", "", out _));
        Assert.Null(registry.GetShortcut("toucan.a"));

        Assert.True(registry.TrySetShortcut("toucan.a", null, out _));
        Assert.Equal("Mod+K", registry.GetShortcut("toucan.a"));
        Assert.Empty(registry.CustomShortcuts);
    }

    [Fact]
    public void ConflictsAreReportedAndCanBeReplaced()
    {
        var registry = Create();
        registry.Register(Def("toucan.a", "Mod+K"), new Handler());
        registry.Register(Def("toucan.b", "Mod+J"), new Handler());

        Assert.False(registry.TrySetShortcut("toucan.b", "Ctrl+K", out var conflicts));
        Assert.Equal("toucan.a", Assert.Single(conflicts).CommandId);
        Assert.Equal("Mod+J", registry.GetShortcut("toucan.b"));

        Assert.True(registry.TrySetShortcut("toucan.b", "Ctrl+K", out _, replaceConflicts: true));
        Assert.Equal("Ctrl+K", registry.GetShortcut("toucan.b"));
        Assert.Null(registry.GetShortcut("toucan.a"));
    }

    [Fact]
    public void SavedShortcutsLoadAndSkipUnknownAndMalformedEntries()
    {
        var registry = Create();
        registry.Register(Def("toucan.a", "Mod+K"), new Handler());
        registry.Register(Def("toucan.b", "Mod+J"), new Handler());

        registry.LoadCustomShortcuts(new Dictionary<string, string>
        {
            ["toucan.a"] = "alt+x",
            ["toucan.b"] = "Mod+",
            ["gone"] = "F1",
        });

        Assert.Equal("Alt+X", registry.GetShortcut("toucan.a"));
        Assert.Equal("Mod+J", registry.GetShortcut("toucan.b"));
        Assert.Single(registry.CustomShortcuts);
    }

    [Fact]
    public void TitlesFallBackFromCultureToParentToDefault()
    {
        var registry = Create();
        registry.Register(new CommandDefinition
        {
            Id = "toucan.a",
            Title = "Pull",
            Category = "Sync",
            LocalizedTitles = new Dictionary<string, string> { ["fr"] = "Tirer", ["pt-BR"] = "Puxar" },
        }, new Handler());

        Assert.Equal("Tirer", registry.GetTitle("toucan.a", new CultureInfo("fr-CA")));
        Assert.Equal("Puxar", registry.GetTitle("toucan.a", new CultureInfo("pt-BR")));
        Assert.Equal("Pull", registry.GetTitle("toucan.a", new CultureInfo("de")));
        Assert.Equal("Sync", registry.GetCategory("toucan.a", new CultureInfo("fr")));
    }
}
