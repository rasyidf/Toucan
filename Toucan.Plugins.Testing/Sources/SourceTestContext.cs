using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Plugins.Sources;

namespace Toucan.Plugins.Testing.Sources;

/// <summary>A session context for tests: a fixed connection, a static or scripted credential, and an HTTP factory you choose.</summary>
public sealed class SourceTestContext : ISourceSessionContext
{
    public SourceTestContext(SourceConnectionInfo? connection = null, ISourceCredentials? credentials = null, ISourceHttpClientFactory? http = null,
        IPluginDiagnostics? diagnostics = null, CancellationToken scope = default)
    {
        Connection = connection ?? new SourceConnectionInfo
        {
            ConnectionId = "con-test",
            AdapterId = FakeSourceAdapter.DefaultId,
            BaseUrl = new Uri("https://source.test/"),
            RemoteProjectId = "fake-project",
            Languages = new Dictionary<string, string> { ["en"] = "en", ["fr"] = "fr", ["de"] = "de" },
        };
        Credentials = credentials ?? new StaticSourceCredentials("test-token");
        Http = http ?? new NoNetworkHttpClientFactory();
        Diagnostics = diagnostics ?? new TestDiagnostics();
        Scope = scope;
    }

    public string AccountId { get; init; } = "acc-test";
    public Uri BaseUrl => Connection.BaseUrl;
    public ISourceCredentials Credentials { get; }
    public ISourceHttpClientFactory Http { get; }
    public IPluginDiagnostics Diagnostics { get; }
    public ILogger Logger { get; init; } = NullLogger.Instance;
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public string WorkspaceId { get; init; } = "test-workspace";
    public string ConnectionId => Connection.ConnectionId;
    public SourceConnectionInfo Connection { get; }
    public CancellationToken Scope { get; }
}

/// <summary>
/// A credential that refreshes to a new token on request. Set <see cref="RefreshTo"/> to the token the fake server accepts
/// after a <see cref="TokenExpiredFault"/>; set <see cref="RefreshFails"/> to make the user sign in again.
/// </summary>
public sealed class StaticSourceCredentials(string token) : ISourceCredentials
{
    private readonly Lock _gate = new();
    private SourceCredential _current = new("Bearer", token);

    public string? RefreshTo { get; set; }
    public bool RefreshFails { get; set; }
    public int Refreshes { get; private set; }

    public Task<SourceCredential> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_current);
    }

    public Task<SourceCredential> RefreshAsync(SourceCredential rejected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Refreshes++;
            if (RefreshFails) throw new SourceException(new SourceError(SourceErrorKind.Authentication, "Sign in again."));
            // Concurrent callers that saw the same rejected credential share one refresh.
            if (!ReferenceEquals(rejected, _current) && rejected != _current) return Task.FromResult(_current);
            _current = new SourceCredential("Bearer", RefreshTo ?? _current.Value);
            return Task.FromResult(_current);
        }
    }
}

/// <summary>An HTTP factory whose clients fail every request, for adapters that should not use the network in a test.</summary>
public sealed class NoNetworkHttpClientFactory : ISourceHttpClientFactory
{
    public int Created { get; private set; }

    public HttpClient Create()
    {
        Created++;
        return new HttpClient(new RefusingHandler(), disposeHandler: true);
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"This test allows no network; the adapter requested {request.RequestUri}.");
    }
}

/// <summary>An HTTP factory over a handler you supply (a stub server), recording every request.</summary>
public sealed class RecordingHttpClientFactory(HttpMessageHandler handler) : ISourceHttpClientFactory
{
    private readonly List<Uri> _requests = [];

    public IReadOnlyList<Uri> Requests { get { lock (_requests) return [.. _requests]; } }

    public HttpClient Create() => new(new Recorder(handler, this), disposeHandler: false);

    private sealed class Recorder(HttpMessageHandler inner, RecordingHttpClientFactory owner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri) lock (owner._requests) owner._requests.Add(uri);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
