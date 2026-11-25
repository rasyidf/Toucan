using System.Net;
using NSubstitute;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins;
using Toucan.Plugins.Sources;
using Toucan.Plugins.Testing.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

public sealed class SourceSignInAndConnectionTests : IAsyncDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-signin-").FullName;
    private readonly MemorySecretService _secrets = new();
    private readonly DiagnosticsService _diagnostics = new();
    private readonly FakeSourceServer _server = new();
    private readonly List<string> _activations = [];
    private StubHandler _auth = new((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private string Workspace => Path.Combine(_root, "project");

    public async ValueTask DisposeAsync()
    {
        if (_connections is not null) await _connections.DisposeAsync();
        if (_activation is not null) await _activation.DisposeAsync();
        _auth.Dispose();
        TempFolder.TryDelete(_root);
    }

    private sealed class OAuthAdapter : IContentSourceAdapter, ISourceAuthExchange
    {
        public Dictionary<string, string>? Fields { get; private set; }

        public SourceAdapterDescriptor Descriptor { get; } = new()
        {
            Id = "acme.svc.api",
            DisplayName = "Acme",
            DefaultBaseUrl = new Uri("https://api.example.com/"),
            AuthMethods =
            [
                new OAuthDeviceCodeAuth("device", "Device", new Uri("https://api.example.com/oauth/device"), new Uri("https://api.example.com/oauth/token"),
                    "client-1", ["read"], new Uri("https://api.example.com/oauth/revoke")),
                new ApiTokenAuth("token", "Token"),
                new CustomAuth("login", "Log in", new ConfigSchema { Fields = [new ConfigField { Key = "user", Label = "User" }, new ConfigField { Key = "password", Label = "Password", Type = ConfigFieldType.Secret }] }),
            ],
            Capabilities = SourceCapabilities.ReadUnits,
        };

        public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SourceCredential> ExchangeAsync(string methodId, IReadOnlyDictionary<string, string> fields, ISourceHttpClientFactory http, CancellationToken cancellationToken)
        {
            Fields = new(fields);
            return Task.FromResult(new SourceCredential("Session", $"session-for-{fields["user"]}"));
        }
    }

    private readonly OAuthAdapter _oauth = new();
    private FakeSourceAdapter? _fake;
    private ISourceAccountStore? _accounts;
    private SourceSignInService? _signIn;
    private PluginActivationService? _activation;
    private SourceConnectionService? _connections;

    private sealed class Probe(string plugin, List<string> log) : IPluginActivator
    {
        public PluginLifetime Lifetime => PluginLifetime.Connection;

        public Task<IAsyncDisposable?> ActivateAsync(IPluginActivationContext context, CancellationToken cancellationToken)
        {
            lock (log) log.Add($"{plugin}:{context.ConnectionId}");
            return Task.FromResult<IAsyncDisposable?>(new End(plugin, context.ConnectionId!, log));
        }

        private sealed class End(string plugin, string connection, List<string> log) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { lock (log) log.Add($"{plugin}:{connection}:closed"); return ValueTask.CompletedTask; }
        }
    }

    private SourceSignInService SignIn(IPluginCatalog? plugins = null)
    {
        _fake = new FakeSourceAdapter(_server);
        var catalog = new ServiceCollectionCatalog([new RegisteredSourceAdapter("test.plugin", _fake), new RegisteredSourceAdapter("acme.svc", _oauth)]);
        _accounts = new SourceAccountStore(Path.Combine(_root, "accounts.json"));
        var broker = new SourceCredentialBroker(_secrets, _diagnostics, Path.Combine(_root, "locks"));
        _signIn = new SourceSignInService(_accounts, broker, catalog, _diagnostics, plugins,
            httpOptions: new SourceHttpOptions { PrimaryHandler = () => _auth, BaseRetryDelay = TimeSpan.FromMilliseconds(1) });
        return _signIn;
    }

    private sealed class ServiceCollectionCatalog(IReadOnlyList<RegisteredSourceAdapter> adapters) : ISourceAdapterCatalog
    {
        public IReadOnlyList<RegisteredSourceAdapter> Adapters { get; } = adapters;
        public RegisteredSourceAdapter? Find(string adapterId) => Adapters.FirstOrDefault(a => string.Equals(a.Descriptor.Id, adapterId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SourceConnectionService> ConnectionsAsync(params SourceConnectionSettings[] connections)
    {
        var signIn = _signIn ?? SignIn();
        _activation = new PluginActivationService([new("test.plugin", "c", new Probe("test.plugin", _activations)), new("other.plugin", "c", new Probe("other.plugin", _activations))]);
        await _activation.OpenWorkspaceAsync(Workspace, Ct);
        _connections = new SourceConnectionService(_activation, new ServiceCollectionCatalog([new RegisteredSourceAdapter("test.plugin", _fake!)]), signIn);
        await _connections.OpenWorkspaceAsync(Workspace, connections.ToList(), Ct);
        return _connections;
    }

    private static SourceConnectionSettings Fake(string id = "con_1", bool connectOnOpen = false) => new()
    {
        Id = id, Adapter = FakeSourceAdapter.DefaultId, RemoteProject = new() { Id = "fake-project" },
        Languages = new() { ["fr-FR"] = "fr" }, Policy = new() { ConnectOnOpen = connectOnOpen },
    };

    // ───────────────────────────── sign-in ─────────────────────────────

    [Fact]
    public async Task ATokenSignInStoresTheCredentialAndRenewingKeepsTheAccount()
    {
        var signIn = SignIn();
        var account = await signIn.SignInWithTokenAsync("acme.svc.api", null, "token", " pasted-token-1234 ");

        Assert.Equal("acme.svc.api", account.Adapter);
        Assert.Equal(new Uri("https://api.example.com/"), account.BaseUrl);
        var credential = await signIn.CredentialsFor(account).GetAsync(Ct);
        Assert.Equal(("Bearer", "pasted-token-1234"), (credential.Scheme, credential.Value));

        var renewed = await signIn.SignInWithTokenAsync("acme.svc.api", null, "token", "second-token-5678", existing: account);
        Assert.Equal(account.Id, renewed.Id);
        Assert.Single(_accounts!.Accounts);
        Assert.Equal("second-token-5678", (await signIn.CredentialsFor(renewed).GetAsync(Ct)).Value);
    }

    [Fact]
    public async Task DeviceSignInThenRefreshAndSignOutWithRevocation()
    {
        var signIn = SignIn();
        _auth = new StubHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/oauth/device" => StubHandler.Json(HttpStatusCode.OK, """{"device_code":"d","user_code":"U-1","verification_uri":"https://api.example.com/activate","interval":0}"""),
            "/oauth/revoke" => new HttpResponseMessage(HttpStatusCode.OK),
            _ => StubHandler.Json(HttpStatusCode.OK, """{"access_token":"at-%%","refresh_token":"rt-1","expires_in":30}""".Replace("%%", Guid.NewGuid().ToString("N")[..6], StringComparison.Ordinal)),
        });

        var account = await signIn.SignInWithDeviceCodeAsync("acme.svc.api", null, "device", _ => Task.CompletedTask, "Work", cancellationToken: Ct);
        var first = signIn.Broker.Read(account)!;
        Assert.Equal("rt-1", first.RefreshToken);

        // It expires within the refresh margin, so using it renews it through the token endpoint.
        var used = await signIn.CredentialsFor(account).GetAsync(Ct);
        Assert.NotEqual(first.Value, used.Value);
        Assert.Contains(_auth.Requests, r => r.Body.Contains("grant_type=refresh_token", StringComparison.Ordinal) && r.Body.Contains("refresh_token=rt-1", StringComparison.Ordinal));

        Assert.True(await signIn.SignOutAsync(account.Id, Ct));
        Assert.Contains(_auth.Requests, r => r.Url.AbsolutePath == "/oauth/revoke" && r.Body.Contains("token=rt-1", StringComparison.Ordinal));
        Assert.Empty(_accounts!.Accounts);
        Assert.Empty(_secrets.Keys("sources/"));
    }

    [Fact]
    public async Task ACustomSignInStoresOnlyTheExchangedCredential()
    {
        var signIn = SignIn();
        var account = await signIn.SignInWithFormAsync("acme.svc.api", null, "login", new Dictionary<string, string> { ["user"] = "ana", ["password"] = "hunter2-very-secret" }, cancellationToken: Ct);

        Assert.Equal("ana", _oauth.Fields!["user"]);
        Assert.Equal(("Session", "session-for-ana"), (signIn.Broker.Read(account)!.Scheme, signIn.Broker.Read(account)!.Value));
        Assert.DoesNotContain(_secrets.Keys(), k => _secrets.GetSecret(k)!.Contains("hunter2", StringComparison.Ordinal));
        Assert.DoesNotContain("hunter2-very-secret", _diagnostics.Redact("pw hunter2-very-secret"), StringComparison.Ordinal);
    }

    [Fact]
    public void AddressesFollowTheAdapterRules()
    {
        var signIn = SignIn();
        var adapter = signIn.Adapter("acme.svc.api");
        Assert.Equal(new Uri("https://api.example.com/x"), SourceSignInService.ResolveBaseUrl(adapter, new Uri("https://api.example.com/x")));
        Assert.Throws<InvalidOperationException>(() => SourceSignInService.ResolveBaseUrl(adapter, new Uri("https://other.example.com/")));
        Assert.Throws<InvalidOperationException>(() => SourceSignInService.ResolveBaseUrl(adapter, new Uri("http://api.example.com/")));
        Assert.Throws<InvalidOperationException>(() => signIn.Adapter("missing.adapter"));
    }

    [Fact]
    public void APluginOnlyReachesAnAccountAddressItDeclared()
    {
        var plugins = Substitute.For<IPluginCatalog>();
        plugins.Plugins.Returns([new PluginLoadResult(_root, PluginStatus.Loaded, new PluginManifest
        {
            Id = "acme.svc", Name = "A", Version = "1.0.0", ApiVersion = "1.3", EntryAssembly = "a.dll", Capabilities = ["sources"],
            Network = new NetworkAccess { Hosts = ["api.example.com"] },
        })]);
        var adapter = SignIn(plugins).Adapter("acme.svc.api");

        Assert.True(_signIn!.PolicyFor(adapter, new Uri("https://api.example.com/")).Allows(new Uri("https://api.example.com/a"), out _));
        Assert.False(_signIn.PolicyFor(adapter, new Uri("https://self.example.org/")).Allows(new Uri("https://self.example.org/a"), out _));
    }

    // ───────────────────────────── connections ─────────────────────────────

    [Fact]
    public async Task AnAdapterConnectionWaitsForSignInThenOpensLazilyForItsPluginOnly()
    {
        var connections = await ConnectionsAsync(Fake());
        Assert.Equal(SourceConnectionState.SignInRequired, connections.Status("con_1")!.State);
        Assert.Empty(_activations);
        var error = await Assert.ThrowsAsync<SourceException>(() => connections.GetSessionAsync("con_1", Ct));
        Assert.Equal(SourceErrorKind.Authentication, error.Error.Kind);

        await _signIn!.SignInWithTokenAsync(FakeSourceAdapter.DefaultId, null, "token", "test-token");
        connections.Refresh();
        Assert.Equal(SourceConnectionState.Disconnected, connections.Status("con_1")!.State);
        Assert.Empty(_activations);

        var test = await connections.TestAsync("con_1", Ct);
        Assert.True(test.Succeeded, test.Message);
        Assert.Equal(SourceConnectionState.Connected, connections.Status("con_1")!.State);
        Assert.Equal(["test.plugin:con_1"], _activations);
        Assert.Equal(1, _fake!.OpenSessions);
        Assert.Same(await connections.GetSessionAsync("con_1", Ct), await connections.GetSessionAsync("con_1", Ct));

        await connections.CloseWorkspaceAsync();
        Assert.Equal(0, _fake.OpenSessions);
        Assert.Contains("test.plugin:con_1:closed", _activations);
    }

    [Fact]
    public async Task SeveralAccountsAskForAChoiceThatIsRemembered()
    {
        var signIn = SignIn();
        var work = await signIn.SignInWithTokenAsync(FakeSourceAdapter.DefaultId, null, "token", "a", "Work");
        await signIn.SignInWithTokenAsync(FakeSourceAdapter.DefaultId, null, "token", "b", "Personal");
        var connections = await ConnectionsAsync(Fake());
        Assert.Equal(SourceConnectionState.ChooseAccount, connections.Status("con_1")!.State);

        connections.ChooseAccount("con_1", work.Id);

        Assert.Equal(SourceConnectionState.Disconnected, connections.Status("con_1")!.State);
        Assert.Equal(work.Id, connections.Status("con_1")!.AccountId);
        Assert.Equal(work.Id, _accounts!.Resolve(FakeSourceAdapter.DefaultId, new Uri("https://source.test/"), Workspace, "con_1").Account!.Id);
    }

    [Fact]
    public async Task ConnectOnOpenAndPluginOwnedConnectionsOpenWithTheProject()
    {
        var signIn = SignIn();
        await signIn.SignInWithTokenAsync(FakeSourceAdapter.DefaultId, null, "token", "t");
        var connections = await ConnectionsAsync(Fake("eager", connectOnOpen: true), Fake("lazy"), new SourceConnectionSettings { Id = "owned", Plugin = "other.plugin" });

        Assert.Equal(SourceConnectionState.Connected, connections.Status("eager")!.State);
        Assert.Equal(SourceConnectionState.Disconnected, connections.Status("lazy")!.State);
        Assert.Equal(SourceConnectionState.Connected, connections.Status("owned")!.State);
        Assert.Equal(["test.plugin:eager", "other.plugin:owned"], _activations);
        var managed = await Assert.ThrowsAsync<SourceException>(() => connections.GetSessionAsync("owned", Ct));
        Assert.Contains("managed by its plugin", managed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedCredentialAsksForSignInAndClosesTheScope()
    {
        var signIn = SignIn();
        await signIn.SignInWithTokenAsync(FakeSourceAdapter.DefaultId, null, "token", "wrong");
        _server.AcceptedToken = "right";
        var connections = await ConnectionsAsync(Fake());

        var test = await connections.TestAsync("con_1", Ct);

        Assert.False(test.Succeeded);
        Assert.Equal(SourceConnectionState.SignInRequired, connections.Status("con_1")!.State);
    }

    [Fact]
    public async Task MissingAdaptersAndOwnerlessConnectionsStayClosed()
    {
        var connections = await ConnectionsAsync(new SourceConnectionSettings { Id = "gone", Adapter = "acme.removed" }, new SourceConnectionSettings { Id = "legacy" });

        Assert.Equal(SourceConnectionState.AdapterUnavailable, connections.Status("gone")!.State);
        Assert.Equal(SourceConnectionState.Unbound, connections.Status("legacy")!.State);
        Assert.Empty(_activations);
        await Assert.ThrowsAsync<SourceException>(() => connections.GetSessionAsync("gone", Ct));
    }

    [Fact]
    public async Task ATokenFromTheEnvironmentConnectsWithoutAnAccount()
    {
        var variable = SourceCredentialBroker.EnvironmentVariableFor("con_ci");
        Environment.SetEnvironmentVariable(variable, "ci-token");
        try
        {
            _server.AcceptedToken = "ci-token";
            var connections = await ConnectionsAsync(Fake("con_ci"));
            Assert.True((await connections.TestAsync("con_ci", Ct)).Succeeded);
            Assert.Empty(_accounts!.Accounts);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task AddingRemovingAndReplacingChangeTheProjectList()
    {
        var signIn = SignIn();
        await signIn.SignInWithTokenAsync(FakeSourceAdapter.DefaultId, null, "token", "t");
        var list = new List<SourceConnectionSettings> { Fake("a") };
        _activation = new PluginActivationService([new("test.plugin", "c", new Probe("test.plugin", _activations))]);
        await _activation.OpenWorkspaceAsync(Workspace, Ct);
        _connections = new SourceConnectionService(_activation, new ServiceCollectionCatalog([new RegisteredSourceAdapter("test.plugin", _fake!)]), signIn);
        await _connections.OpenWorkspaceAsync(Workspace, list, Ct);
        await _connections.GetSessionAsync("a", Ct);

        Assert.True(await _connections.ReplaceAsync("a", Fake("b", connectOnOpen: true), Ct));
        Assert.Equal(["b"], list.Select(c => c.Id));
        Assert.Contains("test.plugin:a:closed", _activations);
        Assert.Equal(SourceConnectionState.Connected, _connections.Status("b")!.State);

        Assert.False(await _connections.ReplaceAsync("b", new SourceConnectionSettings { Id = "c", Adapter = "acme.removed" }, Ct));
        Assert.Empty(list);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _connections.GetSessionAsync("c", Ct));
    }

    [Fact]
    public async Task SessionsSeeTheConnectionMappingAndAScopeThatEndsOnClose()
    {
        var signIn = SignIn();
        await signIn.SignInWithTokenAsync(FakeSourceAdapter.DefaultId, null, "token", "t");
        var capturing = new CapturingAdapter(new FakeSourceAdapter(_server));
        _activation = new PluginActivationService([]);
        await _activation.OpenWorkspaceAsync(Workspace, Ct);
        _connections = new SourceConnectionService(_activation, new ServiceCollectionCatalog([new RegisteredSourceAdapter("test.plugin", capturing)]), signIn);
        await _connections.OpenWorkspaceAsync(Workspace, [Fake()], Ct);

        await _connections.GetSessionAsync("con_1", Ct);
        var context = capturing.Context!;
        Assert.Equal("fake-project", context.Connection.RemoteProjectId);
        Assert.Equal("fr", context.Connection.Languages["fr-FR"]);
        Assert.Equal(Workspace, context.WorkspaceId);
        Assert.False(context.Scope.IsCancellationRequested);

        await _connections.CloseWorkspaceAsync();
        Assert.True(context.Scope.IsCancellationRequested);
    }

    private sealed class CapturingAdapter(FakeSourceAdapter inner) : IContentSourceAdapter
    {
        public ISourceSessionContext? Context { get; private set; }
        public SourceAdapterDescriptor Descriptor => inner.Descriptor;
        public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return inner.OpenSessionAsync(context, cancellationToken);
        }
    }
}
