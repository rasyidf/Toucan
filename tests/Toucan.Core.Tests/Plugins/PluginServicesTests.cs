using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

public sealed class PluginServicesTests : IDisposable
{
    private sealed class MemorySecrets : ISecretService
    {
        public Dictionary<string, string> Values { get; } = [];
        public string? GetSecret(string key) => Values.GetValueOrDefault(key);
        public void SetSecret(string key, string? value) { if (string.IsNullOrEmpty(value)) Values.Remove(key); else Values[key] = value; }
        public bool Remove(string key) => Values.Remove(key);
        public IReadOnlyList<string> Keys(string prefix = "") => [.. Values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Order()];
        public int RemoveAll(string prefix) => Values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList().Count(k => Values.Remove(k));
    }

    private readonly string _root = Directory.CreateTempSubdirectory("toucan-plugin-services-").FullName;
    private readonly MemorySecrets _secretStore = new();
    private readonly PluginHostServices _host = new();

    public PluginServicesTests() => _host.Broker.Attach(new ServiceCollection().AddSingleton<ISecretService>(_secretStore).BuildServiceProvider());

    public void Dispose() => TempFolder.TryDelete(_root);

    private PluginServices Create(string id = "acme.sync") => new(id, _host, _root);

    private static ConfigSchema Schema(int version = 1, params ConfigField[] fields) => new() { Version = version, Fields = fields };

    private static ConfigField Field(string key, ConfigFieldType type = ConfigFieldType.Text, Action<ConfigFieldBuilder>? configure = null)
    {
        var b = new ConfigFieldBuilder { Key = key, Type = type };
        configure?.Invoke(b);
        return b.Build();
    }

    /// <summary>Record "with" cannot be used on required members from here, so fields are assembled through this.</summary>
    private sealed class ConfigFieldBuilder
    {
        public string Key { get; set; } = string.Empty;
        public ConfigFieldType Type { get; set; }
        public ConfigScope Scope { get; set; }
        public object? Default { get; set; }
        public bool Required { get; set; }
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public string? Pattern { get; set; }
        public IReadOnlyList<ConfigChoice> Choices { get; set; } = [];

        public ConfigField Build() => new()
        {
            Key = Key, Label = Key, Type = Type, Scope = Scope, Default = Default, Required = Required, Minimum = Minimum, Maximum = Maximum, Pattern = Pattern, Choices = Choices,
        };
    }

    // ─── storage ───

    [Fact]
    public async Task StorageLivesOutsideThePluginFolderAndRoundTrips()
    {
        var storage = Create().Storage;

        await storage.WriteTextAsync("notes/a.txt", "hello");
        await storage.WriteJsonAsync("state.json", new Dictionary<string, int> { ["n"] = 3 });

        Assert.StartsWith(_root, storage.DataDirectory, StringComparison.Ordinal);
        Assert.EndsWith("acme.sync", storage.DataDirectory, StringComparison.Ordinal);
        Assert.Equal("hello", await storage.ReadTextAsync("notes/a.txt"));
        Assert.Equal(3, (await storage.ReadJsonAsync<Dictionary<string, int>>("state.json"))!["n"]);
        Assert.True(storage.Exists("notes/a.txt"));
        Assert.Null(await storage.ReadTextAsync("missing.txt"));
        Assert.True(storage.Delete("notes/a.txt"));
        Assert.False(storage.Delete("notes/a.txt"));
    }

    [Theory]
    [InlineData("../other/x.txt")]
    [InlineData("a/../../x.txt")]
    [InlineData("/etc/passwd")]
    public void StorageRefusesPathsOutsideItsFolder(string path)
    {
        var storage = Create().Storage;

        Assert.Throws<ArgumentException>(() => storage.ResolvePath(path));
    }

    [Fact]
    public void TwoPluginsGetSeparateFolders() =>
        Assert.NotEqual(Create("acme.a").Storage.DataDirectory, Create("acme.b").Storage.DataDirectory);

    // ─── secrets and redaction ───

    [Fact]
    public async Task SecretsAreScopedToPluginAndTarget()
    {
        var a = Create("acme.a").Secrets;
        var b = Create("acme.b").Secrets;

        await a.SetAsync("token", "app-secret-1");
        await a.SetAsync("token", "ws-secret-2", ConfigTarget.ForWorkspace("/p/one"));
        await a.SetAsync("token", "conn-secret-3", ConfigTarget.ForConnection("main", "/p/one"));
        await b.SetAsync("token", "other-plugin-4");

        Assert.Equal("app-secret-1", await a.GetAsync("token"));
        Assert.Equal("ws-secret-2", await a.GetAsync("token", ConfigTarget.ForWorkspace("/p/one")));
        Assert.Null(await a.GetAsync("token", ConfigTarget.ForWorkspace("/p/two")));
        Assert.Equal("conn-secret-3", await a.GetAsync("token", ConfigTarget.ForConnection("main", "/p/one")));
        Assert.Equal("other-plugin-4", await b.GetAsync("token"));
        Assert.Equal(4, _secretStore.Values.Count);
        // The project's folder path is hashed, never stored in a key.
        Assert.DoesNotContain(_secretStore.Values.Keys, k => k.Contains("/p/one", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASecretThatPassedThroughTheHostIsMaskedEverywhere()
    {
        var services = Create();
        await services.Secrets.SetAsync("token", "s3cr3t-value-123");

        Assert.Equal("login failed for [redacted]", services.Diagnostics.Redact("login failed for s3cr3t-value-123"));
        services.Diagnostics.RegisterSecret("received-from-server");
        Assert.DoesNotContain("received-from-server", services.Diagnostics.Redact("got received-from-server ok"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Authorization: Bearer abcdefghijklmnop.qrstuv", "abcdefghijklmnop")]
    [InlineData("calling https://user:hunter2@example.com/api", "hunter2")]
    [InlineData("api_key=AKIA1234567890 failed", "AKIA1234567890")]
    [InlineData("{\"password\": \"correct horse\"}", "correct")]
    [InlineData("token abcdefghijklmnopqrstuvwxyz0123456789ABCD end", "abcdefghijklmnopqrstuvwxyz0123456789ABCD")]
    public void CredentialLookingTextIsMasked(string text, string leaked)
    {
        var redacted = Create().Diagnostics.Redact(text);

        Assert.DoesNotContain(leaked, redacted, StringComparison.Ordinal);
        Assert.Contains("[redacted]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryTextAndPathsAreLeftAlone()
    {
        var text = "Pulled 12 keys from /projects/app/locales/en.json in 3.2s";

        Assert.Equal(text, Create().Diagnostics.Redact(text));
    }

    [Fact]
    public void TheHomeFolderIsReplaced()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length <= 3) return;

        Assert.Equal("opened ~/Documents/x", Create().Diagnostics.Redact($"opened {home}/Documents/x"));
    }

    [Fact]
    public void WhatAPluginLogsIsMaskedAndKeptForTheReport()
    {
        var inner = new ListLogger();
        var diagnostics = _host.Diagnostics;
        diagnostics.RegisterSecret("tok-123456");
        var logger = new RedactingLogger(inner, diagnostics, "acme.sync");

        logger.LogWarning("Sync failed with tok-123456");
        logger.LogError(new InvalidOperationException("bad password=hunter2"), "Boom");

        Assert.All(inner.Lines, l => Assert.DoesNotContain("tok-123456", l, StringComparison.Ordinal));
        var kept = diagnostics.Recent("acme.sync");
        Assert.Contains(kept, l => l.Level == DiagnosticLevel.Warning && l.Message.Contains("[redacted]", StringComparison.Ordinal));
        Assert.DoesNotContain(kept, l => l.Message.Contains("hunter2", StringComparison.Ordinal));
    }

    private sealed class ListLogger : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }

    [Fact]
    public void TheReportListsPluginsAndTheirLogsWithSecretsMasked()
    {
        _host.Diagnostics.RegisterSecret("tok-987654");
        _host.Diagnostics.Write("acme.sync", DiagnosticLevel.Error, "request failed for tok-987654");
        var catalog = new TestCatalog(new PluginLoadResult("/x/acme.sync", PluginStatus.Loaded,
            new PluginManifest { Id = "acme.sync", Name = "Acme", Version = "1.2.0", ApiVersion = "1.1", EntryAssembly = "a.dll" }));

        var report = _host.Diagnostics.BuildReport(catalog);

        Assert.Contains("acme.sync 1.2.0: Loaded", report, StringComparison.Ordinal);
        Assert.Contains("request failed for [redacted]", report, StringComparison.Ordinal);
        Assert.DoesNotContain("tok-987654", report, StringComparison.Ordinal);
    }

    private sealed class TestCatalog(params PluginLoadResult[] plugins) : IPluginCatalog
    {
        public IReadOnlyList<PluginLoadResult> Plugins { get; } = plugins;
    }

    // ─── configuration ───

    [Fact]
    public void DefaultsApplyUntilSomethingIsStored()
    {
        var config = Create().PluginConfiguration;
        config.SetSchema(Schema(1, Field("interval", ConfigFieldType.WholeNumber, f => f.Default = 30L), Field("name", ConfigFieldType.Text)));

        Assert.Equal(30, config.GetValue<int>("interval"));
        Assert.Null(config.GetValue<string>("name"));
    }

    [Fact]
    public async Task ValidValuesAreStoredAndSurviveARestart()
    {
        var first = Create().PluginConfiguration;
        first.SetSchema(Schema(1, Field("interval", ConfigFieldType.WholeNumber), Field("verbose", ConfigFieldType.Boolean), Field("ratio", ConfigFieldType.Number)));
        Assert.True((await first.SetAsync("interval", 45)).IsValid);
        Assert.True((await first.SetAsync("verbose", true)).IsValid);
        Assert.True((await first.SetAsync("ratio", 0.25)).IsValid);

        var second = Create().PluginConfiguration;
        second.SetSchema(Schema(1, Field("interval", ConfigFieldType.WholeNumber), Field("verbose", ConfigFieldType.Boolean), Field("ratio", ConfigFieldType.Number)));

        Assert.Equal(45, second.GetValue<int>("interval"));
        Assert.True(second.GetValue<bool>("verbose"));
        Assert.Equal(0.25, second.GetValue<double>("ratio"));
    }

    [Theory]
    [InlineData(ConfigFieldType.WholeNumber, 1.5, "whole number")]
    [InlineData(ConfigFieldType.WholeNumber, "abc", "whole number")]
    [InlineData(ConfigFieldType.WholeNumber, 2, "at least")]
    [InlineData(ConfigFieldType.WholeNumber, 99, "at most")]
    [InlineData(ConfigFieldType.Boolean, "yes", "on or off")]
    [InlineData(ConfigFieldType.Url, "ftp://x", "http")]
    [InlineData(ConfigFieldType.Url, "not a url", "http")]
    [InlineData(ConfigFieldType.Choice, "purple", "one of")]
    [InlineData(ConfigFieldType.Text, "ab", "format")]
    public async Task InvalidValuesAreRefusedAndNothingChanges(ConfigFieldType type, object value, string expectedError)
    {
        var config = Create().PluginConfiguration;
        config.SetSchema(Schema(1, Field("x", type, f =>
        {
            f.Minimum = type == ConfigFieldType.WholeNumber ? 5 : null;
            f.Maximum = type == ConfigFieldType.WholeNumber ? 50 : null;
            f.Pattern = type == ConfigFieldType.Text ? "^[0-9]+$" : null;
            f.Choices = type == ConfigFieldType.Choice ? [new ConfigChoice("red", "Red"), new ConfigChoice("blue", "Blue")] : [];
            f.Default = type switch { ConfigFieldType.WholeNumber => 10L, ConfigFieldType.Boolean => false, ConfigFieldType.Choice => "red", ConfigFieldType.Url => "https://a.example", _ => null };
        })));
        var before = config.GetValue<object>("x");

        var result = await config.SetAsync("x", value);

        Assert.False(result.IsValid);
        Assert.Contains(expectedError, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, config.GetValue<object>("x"));
    }

    [Fact]
    public async Task RequiredTextCannotBeEmptyAndNullRestoresTheDefault()
    {
        var config = Create().PluginConfiguration;
        config.SetSchema(Schema(1, Field("name", ConfigFieldType.Text, f => { f.Required = true; f.Default = "main"; })));
        await config.SetAsync("name", "custom");

        Assert.False((await config.SetAsync("name", " ")).IsValid);
        Assert.Equal("custom", config.GetValue<string>("name"));

        Assert.True((await config.SetAsync("name", null)).IsValid);
        Assert.Equal("main", config.GetValue<string>("name"));
    }

    [Fact]
    public async Task ScopesKeepValuesApartAndNeedTheirTarget()
    {
        var config = Create().PluginConfiguration;
        config.SetSchema(Schema(1,
            Field("branch", ConfigFieldType.Text, f => f.Scope = ConfigScope.Workspace),
            Field("endpoint", ConfigFieldType.Url, f => f.Scope = ConfigScope.Connection),
            Field("theme", ConfigFieldType.Text)));

        await config.SetAsync("branch", "main", ConfigTarget.ForWorkspace("/p/one"));
        await config.SetAsync("branch", "dev", ConfigTarget.ForWorkspace("/p/two"));
        await config.SetAsync("endpoint", "https://a.example", ConfigTarget.ForConnection("c1", "/p/one"));
        await config.SetAsync("endpoint", "https://b.example", ConfigTarget.ForConnection("c2"));

        Assert.Equal("main", config.GetValue<string>("branch", ConfigTarget.ForWorkspace("/p/one")));
        Assert.Equal("dev", config.GetValue<string>("branch", ConfigTarget.ForWorkspace("/p/two")));
        Assert.Equal("https://a.example", config.GetValue<string>("endpoint", ConfigTarget.ForConnection("c1", "/p/one")));
        Assert.Null(config.GetValue<string>("endpoint", ConfigTarget.ForConnection("c1")));
        Assert.Throws<ArgumentException>(() => config.GetValue<string>("branch")); // a workspace setting needs a project
        Assert.Throws<ArgumentException>(() => config.GetValue<string>("theme", ConfigTarget.ForWorkspace("/p/one"))); // wrong scope
        Assert.DoesNotContain("/p/one", await File.ReadAllTextAsync(Path.Combine(Create().Storage.DataDirectory, "config.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecretSettingsGoToTheSecretStoreNotTheFile()
    {
        var services = Create();
        var config = services.PluginConfiguration;
        config.SetSchema(Schema(1, Field("apiKey", ConfigFieldType.Secret), Field("name", ConfigFieldType.Text)));

        await config.SetAsync("apiKey", "very-secret-key-1");
        await config.SetAsync("name", "x");

        Assert.Null(config.GetValue<string>("apiKey")); // never returned by GetValue
        Assert.Equal("very-secret-key-1", await config.GetSecretAsync("apiKey"));
        Assert.DoesNotContain("very-secret-key-1", await File.ReadAllTextAsync(Path.Combine(services.Storage.DataDirectory, "config.json")), StringComparison.Ordinal);
        Assert.Contains("very-secret-key-1", _secretStore.Values.Values);
        await Assert.ThrowsAsync<ArgumentException>(() => config.GetSecretAsync("name"));
    }

    [Fact]
    public async Task ChangesAreAnnounced()
    {
        var config = Create().PluginConfiguration;
        config.SetSchema(Schema(1, Field("name", ConfigFieldType.Text)));
        var seen = new List<string>();
        config.Changed += (_, e) => seen.Add(e.Key);

        await config.SetAsync("name", "a");
        await config.SetAsync("name", 42); // refused

        Assert.Equal(["name"], seen);
    }

    [Fact]
    public async Task OldValuesAreMigratedOnceWhenTheSchemaVersionRises()
    {
        var v1 = Create().PluginConfiguration;
        v1.SetSchema(Schema(1, Field("serverUrl", ConfigFieldType.Url), Field("pollSeconds", ConfigFieldType.WholeNumber)));
        await v1.SetAsync("serverUrl", "https://old.example");
        await v1.SetAsync("pollSeconds", 30);

        var v2Schema = new ConfigSchema
        {
            Version = 2,
            Fields = [Field("endpoint", ConfigFieldType.Url), Field("pollSeconds", ConfigFieldType.WholeNumber)],
            Migrations =
            [
                new ConfigMigration(1, (scope, bag) =>
                {
                    if (bag.Remove("serverUrl", out var url)) bag["endpoint"] = url;
                }),
            ],
        };
        var v2 = Create().PluginConfiguration;
        v2.SetSchema(v2Schema);

        Assert.Equal("https://old.example", v2.GetValue<string>("endpoint"));
        Assert.Equal(30, v2.GetValue<int>("pollSeconds"));

        // Written back as version 2: a third start does not run the step again.
        var text = await File.ReadAllTextAsync(Path.Combine(Create().Storage.DataDirectory, "config.json"));
        Assert.Contains("\"schemaVersion\": 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("serverUrl", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASkippedMigrationFallsBackToDefaultsForValuesThatNoLongerFit()
    {
        var v1 = Create().PluginConfiguration;
        v1.SetSchema(Schema(1, Field("mode", ConfigFieldType.Text)));
        await v1.SetAsync("mode", "legacy");

        var v2 = Create().PluginConfiguration;
        v2.SetSchema(Schema(2, Field("mode", ConfigFieldType.Choice, f => { f.Choices = [new ConfigChoice("fast", "Fast"), new ConfigChoice("safe", "Safe")]; f.Default = "safe"; })));

        Assert.Equal("safe", v2.GetValue<string>("mode"));
        Assert.Contains(_host.Diagnostics.Recent("acme.sync"), l => l.Message.Contains("No settings migration", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettingsSavedByANewerPluginAreKeptAndReported()
    {
        var v2 = Create().PluginConfiguration;
        v2.SetSchema(Schema(2, Field("mode", ConfigFieldType.Text)));
        await v2.SetAsync("mode", "x");

        var v1 = Create().PluginConfiguration;
        v1.SetSchema(Schema(1, Field("mode", ConfigFieldType.Text)));
        await v1.SetAsync("mode", "y");

        Assert.Contains(_host.Diagnostics.Recent("acme.sync"), l => l.Message.Contains("newer version of this plugin", StringComparison.Ordinal));
        var text = await File.ReadAllTextAsync(Path.Combine(Create().Storage.DataDirectory, "config.json"));
        Assert.Contains("\"schemaVersion\": 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMigrationThatCanNeverRunIsReportedToTheAuthor() =>
        Assert.Throws<PluginRegistrationException>(() => Create().PluginConfiguration.SetSchema(new ConfigSchema
        {
            Version = 2,
            Fields = [Field("a")],
            Migrations = [new ConfigMigration(2, (_, _) => { })],
        }));

    [Fact]
    public async Task AnUnreadableSettingsFileIsResetNotFatal()
    {
        var services = Create();
        await services.Storage.WriteTextAsync("config.json", "{ not json");
        var config = services.PluginConfiguration;
        config.SetSchema(Schema(1, Field("n", ConfigFieldType.WholeNumber, f => f.Default = 7L)));

        Assert.Equal(7, config.GetValue<int>("n"));
        Assert.Contains(_host.Diagnostics.Recent("acme.sync"), l => l.Message.Contains("reset to defaults", StringComparison.Ordinal));
    }

    [Fact]
    public void MistakesInASchemaAreReportedToTheAuthor()
    {
        Assert.Throws<PluginRegistrationException>(() => Create().PluginConfiguration.SetSchema(Schema(1, Field("a"), Field("a"))));
        Assert.Throws<PluginRegistrationException>(() => Create().PluginConfiguration.SetSchema(Schema(1, Field("bad key"))));
        Assert.Throws<PluginRegistrationException>(() => Create().PluginConfiguration.SetSchema(Schema(1, Field("c", ConfigFieldType.Choice))));
        Assert.Throws<PluginRegistrationException>(() => Create().PluginConfiguration.SetSchema(Schema(1, Field("s", ConfigFieldType.Secret, f => f.Default = "x"))));
        Assert.Throws<PluginRegistrationException>(() => Create().PluginConfiguration.SetSchema(Schema(1, Field("p", ConfigFieldType.Text, f => f.Pattern = "(["))));
        Assert.Throws<PluginRegistrationException>(() => Create().PluginConfiguration.SetSchema(Schema(1, Field("n", ConfigFieldType.WholeNumber, f => { f.Minimum = 5; f.Default = 1L; }))));

        var once = Create().PluginConfiguration;
        once.SetSchema(Schema());
        Assert.Throws<PluginRegistrationException>(() => once.SetSchema(Schema()));
    }

    [Fact]
    public async Task ServicesRefuseToRunBeforeTheHostIsReady()
    {
        var early = new PluginHostServices();
        var services = new PluginServices("acme.sync", early, _root);

        // Files need nothing from the container; secrets do.
        Assert.NotEmpty(services.Storage.DataDirectory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.Secrets.GetAsync("x"));
    }

    // ─── notifications ───

    [Fact]
    public void NotificationsAreKeptAndAnnouncedWithTextMasked()
    {
        var services = Create();
        services.Diagnostics.RegisterSecret("tok-555555");
        var shown = new List<HostNotification>();
        _host.Notifications.Notified += (_, e) => shown.Add(e.Notification);

        services.Notifier.Notify(new PluginNotification { Title = "Pull failed", Message = "server said tok-555555", Severity = NotificationSeverity.Error, ActionCommandId = "acme.sync.retry", ActionLabel = "Retry" });

        var entry = Assert.Single(shown);
        Assert.Equal("acme.sync", entry.Source);
        Assert.Equal("server said [redacted]", entry.Content.Message);
        Assert.Equal("acme.sync.retry", entry.Content.ActionCommandId);
        Assert.Contains(entry, _host.Notifications.History);
    }

    // ─── background operations ───

    [Fact]
    public async Task AnOperationReportsProgressAndCompletes()
    {
        var ops = Create().Operations;
        var gate = new TaskCompletionSource();
        var handle = ops.Start(new OperationOptions { Title = "Pull" }, async (ctx, ct) =>
        {
            ctx.Report("half way", 0.5);
            await gate.Task;
        });

        var operation = Assert.Single(_host.Operations.Active);
        Assert.Equal("Pull", operation.Title);
        await WaitUntil(() => operation.Fraction == 0.5);
        Assert.Equal("half way", operation.Message);
        Assert.Equal(OperationStatus.Running, handle.Status);
        Assert.Single(ops.Active);

        gate.SetResult();
        await handle.Completion;

        Assert.Equal(OperationStatus.Completed, handle.Status);
        Assert.Empty(_host.Operations.Active);
    }

    [Fact]
    public async Task AFailingOperationIsReportedWithSecretsMaskedAndNeverThrows()
    {
        var services = Create();
        services.Diagnostics.RegisterSecret("tok-777777");

        var handle = services.Operations.Start(new OperationOptions { Title = "Push" }, (_, _) => throw new InvalidOperationException("denied for tok-777777"));
        await handle.Completion;

        var operation = (BackgroundOperation)handle;
        Assert.Equal(OperationStatus.Failed, handle.Status);
        Assert.Equal("InvalidOperationException: denied for [redacted]", operation.FailureMessage);
    }

    [Fact]
    public async Task CancellingStopsAnOperation()
    {
        var handle = Create().Operations.Start(new OperationOptions { Title = "Long" }, (_, ct) => Task.Delay(Timeout.Infinite, ct));

        handle.Cancel();
        await handle.Completion;

        Assert.Equal(OperationStatus.Cancelled, handle.Status);
    }

    [Fact]
    public async Task ClosingTheProjectCancelsOnlyOperationsThatAskedForIt()
    {
        var ops = Create().Operations;
        var tied = ops.Start(new OperationOptions { Title = "Tied" }, (_, ct) => Task.Delay(Timeout.Infinite, ct));
        var free = ops.Start(new OperationOptions { Title = "Free", CancelWithWorkspace = false }, (_, ct) => Task.Delay(Timeout.Infinite, ct));

        _host.Operations.CancelForWorkspaceClose();
        await tied.Completion;

        Assert.Equal(OperationStatus.Cancelled, tied.Status);
        Assert.Equal(OperationStatus.Running, free.Status);
        free.Cancel();
        await free.Completion;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }
}
