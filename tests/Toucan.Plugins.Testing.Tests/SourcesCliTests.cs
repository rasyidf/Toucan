using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Toucan.Sample.SourceServer;
using Xunit;

namespace Toucan.Plugins.Testing.Tests;

/// <summary>
/// <c>toucan sources status|pull|push|sync</c> as a real process against the reference server: plans, confirmation, exit codes,
/// conflicts, tokens from the environment. The process runs with its own home folder, so it never reads or writes the developer's data.
/// </summary>
public sealed class SourcesCliTests : IAsyncLifetime
{
    private readonly string _home = Directory.CreateTempSubdirectory("toucan-cli-").FullName;
    private readonly SourceStore _store = ReferenceServer.Seeded();
    private readonly string _project;
    private WebApplication _app = null!;
    private Uri _url = null!;

    public SourcesCliTests() => _project = Directory.CreateDirectory(Path.Combine(_home, "project")).FullName;

    public async ValueTask InitializeAsync()
    {
        _app = ReferenceServer.Build(["--urls", "http://127.0.0.1:0"], _store);
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _url = new Uri(_app.Urls.First());
        File.WriteAllText(Path.Combine(_project, "en.json"), """{"home":{"title":"Welcome","subtitle":"Translate anything"},"cart":{"empty":"Your cart is empty"}}""");
        File.WriteAllText(Path.Combine(_project, "fr.json"), """{"home":{"title":"Bienvenue","subtitle":"Traduisez tout"}}""");
        File.WriteAllText(Path.Combine(_project, "toucan.tproj"), $$"""
            { "primaryLanguage": "en", "languages": ["en", "fr"], "framework": "generic-json",
              "sources": [ { "id": "mem", "name": "Reference", "adapter": "toucan.sources.rest", "baseUrl": "{{_url}}",
                             "remoteProject": { "id": "demo" }, "languages": { "en": "en", "fr": "fr" }, "resources": [ { "remote": "app" } ] } ] }
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        try { Directory.Delete(_home, recursive: true); }
        catch (IOException) { }
    }

    private static string CliDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Toucan.CrossPlatform.slnx"))) dir = dir.Parent;
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(dir!.FullName, "build", "Toucan.CLI", configuration, "net10.0", "toucan.dll");
    }

    private sealed record Run(int Exit, string Out, string Err);

    private async Task<Run> CliAsync(string token, params string[] args)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        info.ArgumentList.Add(CliDll());
        foreach (var a in args) info.ArgumentList.Add(a);
        foreach (var name in new[] { "HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "APPDATA", "LOCALAPPDATA", "USERPROFILE" }) info.Environment[name] = _home;
        info.Environment["TOUCAN_SECRET_KEY_STORE"] = "file";
        info.Environment["TOUCAN_PLUGINS_DIR"] = Path.Combine(_home, "plugins");
        info.Environment["TOUCAN_PLUGIN_POLICY"] = Path.Combine(_home, "policy.json");
        info.Environment["TOUCAN_SOURCE_TOKEN_MEM"] = token;
        using var process = Process.Start(info)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        await process.WaitForExitAsync(timeout.Token);
        return new(process.ExitCode, await output, await error);
    }

    private Task<Run> SourcesAsync(params string[] args) => CliAsync(ReferenceServer.DefaultToken, ["sources", .. args]);

    private string French(string key) => JsonDocument.Parse(File.ReadAllText(Path.Combine(_project, "fr.json"))).RootElement.GetProperty("home").GetProperty(key).GetString()!;

    // ───────────── tests ─────────────

    [Fact]
    public async Task StatusBeforeTheFirstSyncSaysSoAndChangesNothing()
    {
        var run = await SourcesAsync("status", "mem", _project, "--json");

        Assert.True(run.Exit == 0, run.Err + run.Out);
        var json = JsonDocument.Parse(run.Out).RootElement;
        Assert.True(json.GetProperty("firstSync").GetBoolean());
        Assert.Equal(0, json.GetProperty("boundUnits").GetInt32());
    }

    [Fact]
    public async Task PlanPrintsTheFirstSyncAndAppliesNothing()
    {
        _store.Put("app", "home.title", "fr", "Accueil");

        var run = await SourcesAsync("sync", "mem", _project, "--plan", "--json");

        var plan = JsonDocument.Parse(run.Out).RootElement;
        Assert.True(run.Exit == 2, run.Err + run.Out);
        Assert.True(plan.GetProperty("firstSync").GetBoolean());
        Assert.Equal("home.title", plan.GetProperty("conflicts")[0].GetProperty("key").GetString());
        Assert.Equal("Bienvenue", French("title"));
        Assert.False(Directory.Exists(Path.Combine(_project, ".toucan", "local", "sources", "mem")));
    }

    [Fact]
    public async Task ANonInteractiveRunWithoutYesRefusesToApply()
    {
        _store.Put("app", "home.subtitle", "fr", "Changed there");
        await SourcesAsync("sync", "mem", _project, "--yes", "--take", "remote");

        _store.Put("app", "home.subtitle", "fr", "Changed again");
        var run = await SourcesAsync("pull", "mem", _project);

        Assert.True(run.Exit == 1, run.Err + run.Out);
        Assert.Contains("Refusing to apply without --yes", run.Err, StringComparison.Ordinal);
        Assert.Equal("Changed there", French("subtitle"));
    }

    [Fact]
    public async Task FirstSyncWithTakeRemoteBindsTheProjectAndPullsLaterChanges()
    {
        _store.Put("app", "home.title", "fr", "Accueil");
        var first = await SourcesAsync("sync", "mem", _project, "--yes", "--take", "remote");
        Assert.True(first.Exit == 0, first.Err + first.Out);
        Assert.Equal("Accueil", French("title"));

        var status = JsonDocument.Parse((await SourcesAsync("status", "mem", _project, "--json")).Out).RootElement;
        Assert.False(status.GetProperty("firstSync").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, status.GetProperty("lastPull").ValueKind);

        _store.Put("app", "home.subtitle", "fr", "Tout traduire");
        var pull = await SourcesAsync("pull", "mem", _project, "--yes");
        Assert.True(pull.Exit == 0, pull.Err + pull.Out);
        Assert.Equal("Tout traduire", French("subtitle"));

        var again = await SourcesAsync("pull", "mem", _project, "--yes", "--json");
        Assert.True(again.Exit == 0, again.Err + again.Out);
        Assert.Empty(JsonDocument.Parse(again.Out).RootElement.GetProperty("pull").EnumerateArray());
    }

    [Fact]
    public async Task LocalEditsArePushedAndReportedInJson()
    {
        await SourcesAsync("sync", "mem", _project, "--yes", "--take", "remote");
        File.WriteAllText(Path.Combine(_project, "fr.json"), """{"home":{"title":"Bienvenue ici","subtitle":"Traduisez tout"}}""");

        var run = await SourcesAsync("push", "mem", _project, "--yes", "--json");

        Assert.True(run.Exit == 0, run.Err + run.Out);
        Assert.Equal("Bienvenue ici", _store.Get("app", "home.title", "fr")!.Value);
        Assert.Equal(1, JsonDocument.Parse(run.Out).RootElement.GetProperty("applied").GetProperty("pushed").GetInt32());
    }

    [Fact]
    public async Task ConflictsNeverResolveThemselvesAndExitWithTwo()
    {
        await SourcesAsync("sync", "mem", _project, "--yes", "--take", "remote");
        File.WriteAllText(Path.Combine(_project, "fr.json"), """{"home":{"title":"Mine","subtitle":"Traduisez tout"}}""");
        _store.Put("app", "home.title", "fr", "Theirs");

        var plan = await SourcesAsync("sync", "mem", _project, "--plan");
        Assert.True(plan.Exit == 2, plan.Err + plan.Out);
        Assert.Contains("conflict", plan.Out, StringComparison.Ordinal);

        var run = await SourcesAsync("sync", "mem", _project, "--yes");
        Assert.True(run.Exit == 2, run.Err + run.Out);
        Assert.Equal("Mine", French("title"));
        Assert.Equal("Theirs", _store.Get("app", "home.title", "fr")!.Value);

        var taken = await SourcesAsync("sync", "mem", _project, "--yes", "--take", "local");
        Assert.True(taken.Exit == 0, taken.Err + taken.Out);
        Assert.Equal("Mine", _store.Get("app", "home.title", "fr")!.Value);
    }

    [Fact]
    public async Task AWrongTokenExitsWithThreeAndPointsToLogin()
    {
        var run = await CliAsync("wrong", "sources", "sync", "mem", _project, "--plan");

        Assert.True(run.Exit == 3, run.Err + run.Out);
        Assert.Contains("toucan sources login mem", run.Err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownConnectionAndABadTakeValueAreUsageErrors()
    {
        var unknown = await SourcesAsync("pull", "nope", _project);
        Assert.True(unknown.Exit == 1, unknown.Err + unknown.Out);
        Assert.Contains("no connection 'nope'", unknown.Err, StringComparison.Ordinal);

        var bad = await SourcesAsync("sync", "mem", _project, "--take", "both");
        Assert.True(bad.Exit == 1, bad.Err + bad.Out);
        Assert.Contains("--take must be", bad.Err, StringComparison.Ordinal);

        var missing = await SourcesAsync("pull");
        Assert.True(missing.Exit == 1, missing.Err + missing.Out);
    }

    [Fact]
    public async Task TheTokenIsNeverWrittenToTheProjectOrTheLogs()
    {
        var run = await SourcesAsync("sync", "mem", _project, "--yes", "--take", "remote");

        Assert.DoesNotContain(ReferenceServer.DefaultToken, run.Out + run.Err, StringComparison.Ordinal);
        foreach (var file in Directory.GetFiles(_project, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(ReferenceServer.DefaultToken, File.ReadAllText(file), StringComparison.Ordinal);
    }
}
