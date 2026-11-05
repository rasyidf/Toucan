using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

public enum SourceConnectionState
{
    /// <summary>The connection names no adapter or plugin (written by an older build); choose one to use it.</summary>
    Unbound,

    /// <summary>No loaded plugin provides the adapter (missing, untrusted or disabled).</summary>
    AdapterUnavailable,

    /// <summary>No account for this service, or its sign-in expired: the user signs in.</summary>
    SignInRequired,

    /// <summary>Several accounts fit: the user picks one for this connection.</summary>
    ChooseAccount,

    /// <summary>Ready; the session opens on first use.</summary>
    Disconnected,
    Connecting,
    Connected,
    Failed,
}

public sealed record SourceConnectionStatus(string ConnectionId, SourceConnectionState State, string Message, string? AccountId = null)
{
    /// <summary>The short form older APIs report: "Connected", "Connecting", "Failed: why", or the state and message.</summary>
    public string Summary => State switch
    {
        SourceConnectionState.Connected or SourceConnectionState.Connecting => State.ToString(),
        SourceConnectionState.Failed => $"Failed: {Message}",
        _ => Message.Length > 0 ? $"{State}: {Message}" : State.ToString(),
    };
}

public sealed class SourceConnectionStatusChangedEventArgs(SourceConnectionStatus status) : EventArgs
{
    public SourceConnectionStatus Status { get; } = status;
}

/// <summary>
/// The open project's content-source connections: who owns each, which account it uses, its state, and its live session.
/// Adapter connections open lazily on first use (or on project open with <c>policy.connectOnOpen</c>); plugin-owned
/// connections open with the project. Each connection has its own plugin scope; a failure affects only that connection.
/// </summary>
public interface ISourceConnectionService
{
    string? WorkspaceId { get; }
    IReadOnlyList<SourceConnectionStatus> Connections { get; }
    SourceConnectionStatus? Status(string connectionId);
    event EventHandler<SourceConnectionStatusChangedEventArgs>? StatusChanged;

    /// <summary>Takes the project's connections (the live list, so additions are saved with the project) and opens those that open with it.</summary>
    Task OpenWorkspaceAsync(string workspaceId, IList<SourceConnectionSettings> connections, CancellationToken cancellationToken = default);

    /// <summary>The connection's session, opening it first when needed. Throws <see cref="SourceException"/> when it cannot.</summary>
    Task<IContentSourceSession> GetSessionAsync(string connectionId, CancellationToken cancellationToken = default);

    /// <summary>Opens the connection if needed and checks that the credential and remote project work.</summary>
    Task<SourceTestResult> TestAsync(string connectionId, CancellationToken cancellationToken = default);

    Task DisconnectAsync(string connectionId);
    Task AddAsync(SourceConnectionSettings connection, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string connectionId);

    /// <summary>Closes one connection and opens its replacement without touching the project or other connections.</summary>
    Task<bool> ReplaceAsync(string oldConnectionId, SourceConnectionSettings replacement, CancellationToken cancellationToken = default);

    /// <summary>Uses <paramref name="accountId"/> for the connection from now on (remembered on this machine).</summary>
    void ChooseAccount(string connectionId, string accountId);

    /// <summary>Re-checks accounts, for example after a sign-in.</summary>
    void Refresh();

    /// <summary>Ends every session and connection scope, before the project's plugin scope closes.</summary>
    Task CloseWorkspaceAsync();
}

public sealed class SourceConnectionService : ISourceConnectionService, IAsyncDisposable, IDisposable
{
    private readonly IPluginActivationService? _activation;
    private readonly ISourceAdapterCatalog? _adapters;
    private readonly SourceSignInService? _signIn;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private IList<SourceConnectionSettings> _settings = [];
    private CancellationTokenSource _workspaceScope = new();

    private sealed class Entry(SourceConnectionSettings settings)
    {
        public SourceConnectionSettings Settings { get; set; } = settings;
        public SourceConnectionStatus Status { get; set; } = new(settings.Id, SourceConnectionState.Disconnected, string.Empty);
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? OwnerPluginId { get; set; }
        public bool ScopeOpen { get; set; }
        public IContentSourceSession? Session { get; set; }
        public SourceHttpClientFactory? Http { get; set; }
        public CancellationTokenSource? Scope { get; set; }
        public SourceAccount? Account { get; set; }
    }

    /// <param name="activation">Opens and closes plugin connection scopes; null runs no plugin code.</param>
    /// <param name="adapters">Null supports plugin-owned connections only.</param>
    /// <param name="signIn">Accounts and credentials; null supports plugin-owned connections only.</param>
    public SourceConnectionService(IPluginActivationService? activation, ISourceAdapterCatalog? adapters = null, SourceSignInService? signIn = null,
        ILogger<SourceConnectionService>? logger = null)
    {
        _activation = activation;
        _adapters = adapters;
        _signIn = signIn;
        _logger = logger ?? NullLogger<SourceConnectionService>.Instance;
    }

    public string? WorkspaceId { get; private set; }

    /// <summary>False only resolves owners and accounts when a project opens, for listing; nothing connects then.</summary>
    public bool OpensConnections { get; init; } = true;

    public IReadOnlyList<SourceConnectionStatus> Connections { get { lock (_gate) return [.. _entries.Values.Select(e => e.Status)]; } }

    public SourceConnectionStatus? Status(string connectionId) { lock (_gate) return _entries.GetValueOrDefault(connectionId)?.Status; }

    public event EventHandler<SourceConnectionStatusChangedEventArgs>? StatusChanged;

    public async Task OpenWorkspaceAsync(string workspaceId, IList<SourceConnectionSettings> connections, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentNullException.ThrowIfNull(connections);
        if (WorkspaceId is not null) await CloseWorkspaceAsync().ConfigureAwait(false);
        lock (_gate)
        {
            WorkspaceId = workspaceId;
            _settings = connections;
            foreach (var connection in connections.Where(c => !string.IsNullOrWhiteSpace(c.Id)))
                _entries[connection.Id] = new Entry(connection);
        }
        foreach (var entry in Snapshot()) Resolve(entry);
        foreach (var entry in Snapshot().Where(e => OpensConnections && OpensWithProject(e)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ConnectQuietlyAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IContentSourceSession> GetSessionAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        var entry = Find(connectionId);
        await ConnectAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry.Session ?? throw new SourceException(new SourceError(SourceErrorKind.Unsupported,
            $"Connection {connectionId} is managed by its plugin and has no content-source session."));
    }

    public async Task<SourceTestResult> TestAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await GetSessionAsync(connectionId, cancellationToken).ConfigureAwait(false);
            var result = await session.TestAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded && result.Error?.Kind == SourceErrorKind.Authentication)
                SetStatus(Find(connectionId), SourceConnectionState.SignInRequired, result.Message);
            return result;
        }
        catch (SourceException ex)
        {
            return SourceTestResult.Failed(ex.Error);
        }
    }

    public async Task DisconnectAsync(string connectionId)
    {
        var entry = Find(connectionId);
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await TearDownAsync(entry).ConfigureAwait(false);
            Resolve(entry);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task AddAsync(SourceConnectionSettings connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Id);
        if (WorkspaceId is null) throw new InvalidOperationException("No project is open.");
        Entry entry;
        lock (_gate)
        {
            if (_entries.ContainsKey(connection.Id)) throw new InvalidOperationException($"The project already has a connection {connection.Id}.");
            entry = new Entry(connection);
            _entries[connection.Id] = entry;
            if (!_settings.Contains(connection)) _settings.Add(connection);
        }
        Resolve(entry);
        if (OpensWithProject(entry)) await ConnectQuietlyAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string connectionId)
    {
        Entry? entry;
        lock (_gate) entry = _entries.GetValueOrDefault(connectionId);
        if (entry is null) return false;
        await entry.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await TearDownAsync(entry).ConfigureAwait(false);
        }
        finally
        {
            entry.Gate.Release();
        }
        lock (_gate)
        {
            _entries.Remove(connectionId);
            for (var i = _settings.Count - 1; i >= 0; i--) if (_settings[i].Id == connectionId) _settings.RemoveAt(i);
        }
        return true;
    }

    public async Task<bool> ReplaceAsync(string oldConnectionId, SourceConnectionSettings replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (!string.IsNullOrWhiteSpace(oldConnectionId)) await RemoveAsync(oldConnectionId).ConfigureAwait(false);
        await AddAsync(replacement, cancellationToken).ConfigureAwait(false);
        var status = Status(replacement.Id)!;
        if (status.State is SourceConnectionState.Failed or SourceConnectionState.AdapterUnavailable or SourceConnectionState.Unbound)
        {
            await RemoveAsync(replacement.Id).ConfigureAwait(false);
            return false;
        }
        return true;
    }

    public void ChooseAccount(string connectionId, string accountId)
    {
        if (_signIn is null || WorkspaceId is null) throw new InvalidOperationException("Accounts are not available in this host.");
        var entry = Find(connectionId);
        _signIn.Accounts.Bind(WorkspaceId, connectionId, accountId);
        if (entry.Session is null) Resolve(entry);
    }

    public void Refresh()
    {
        foreach (var entry in Snapshot().Where(e => e.Session is null && e.Status.State is not SourceConnectionState.Connecting)) Resolve(entry);
    }

    public async Task CloseWorkspaceAsync()
    {
        CancellationTokenSource scope;
        lock (_gate)
        {
            // Closing twice (project close, then host shutdown) is a no-op.
            if (WorkspaceId is null && _entries.Count == 0) return;
            scope = _workspaceScope;
            _workspaceScope = new CancellationTokenSource();
        }
        await scope.CancelAsync().ConfigureAwait(false);
        foreach (var entry in Snapshot())
        {
            await entry.Gate.WaitAsync().ConfigureAwait(false);
            try { await TearDownAsync(entry).ConfigureAwait(false); }
            finally { entry.Gate.Release(); }
        }
        lock (_gate)
        {
            _entries.Clear();
            _settings = [];
            WorkspaceId = null;
        }
        scope.Dispose();
    }

    public async ValueTask DisposeAsync() => await CloseWorkspaceAsync().ConfigureAwait(false);

    // Containers built without async disposal call this; Task.Run keeps a UI sync context from deadlocking it.
    public void Dispose() => Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();

    // ───────────────────────────── internals ─────────────────────────────

    private List<Entry> Snapshot() { lock (_gate) return [.. _entries.Values]; }

    private Entry Find(string connectionId)
    {
        lock (_gate)
            return _entries.GetValueOrDefault(connectionId) ?? throw new InvalidOperationException($"The project has no connection {connectionId}.");
    }

    private static bool OpensWithProject(Entry entry) =>
        entry.Status.State == SourceConnectionState.Disconnected && (string.IsNullOrWhiteSpace(entry.Settings.Adapter) || entry.Settings.Policy.ConnectOnOpen);

    /// <summary>Works out the owner and account without opening anything.</summary>
    private void Resolve(Entry entry)
    {
        var settings = entry.Settings;
        if (!string.IsNullOrWhiteSpace(settings.Adapter))
        {
            var adapter = _adapters?.Find(settings.Adapter);
            if (adapter is null || _signIn is null)
            {
                SetStatus(entry, SourceConnectionState.AdapterUnavailable, $"No loaded plugin provides {settings.Adapter}.");
                return;
            }
            entry.OwnerPluginId = adapter.PluginId;
            Uri address;
            try { address = SourceSignInService.ResolveBaseUrl(adapter, settings.BaseUrl); }
            catch (InvalidOperationException ex) { SetStatus(entry, SourceConnectionState.Failed, ex.Message); return; }

            if (adapter.Descriptor.AuthMethods.All(m => m is NoSignInAuth))
            {
                // A service that needs no sign-in (a local folder) gets a stand-in account that holds no credential.
                entry.Account = new SourceAccount { Id = "anonymous-" + settings.Id, Adapter = adapter.Descriptor.Id, BaseUrl = address,
                    AuthMethod = adapter.Descriptor.AuthMethods[0].Id, DisplayName = "This computer" };
                SetStatus(entry, SourceConnectionState.Disconnected, string.Empty, entry.Account.Id);
                return;
            }
            var match = _signIn.Accounts.Resolve(adapter.Descriptor.Id, address, WorkspaceId!, settings.Id);
            entry.Account = match.Account ?? EnvironmentAccount(adapter, address, settings.Id);
            if (entry.Account is not null)
                SetStatus(entry, SourceConnectionState.Disconnected, string.Empty, entry.Account.Id);
            else if (match.Resolution == AccountResolution.Ambiguous)
                SetStatus(entry, SourceConnectionState.ChooseAccount, $"Choose which of {match.Candidates.Count} accounts to use.");
            else
                SetStatus(entry, SourceConnectionState.SignInRequired, $"Sign in to {adapter.Descriptor.DisplayName} ({address.Host}).");
            return;
        }
        if (!string.IsNullOrWhiteSpace(settings.Plugin))
        {
            entry.OwnerPluginId = settings.Plugin;
            SetStatus(entry, SourceConnectionState.Disconnected, string.Empty);
            return;
        }
        SetStatus(entry, SourceConnectionState.Unbound, "This connection names no adapter or plugin.");
    }

    /// <summary>In CI, a token in the environment stands in for a stored account and is never saved.</summary>
    private static SourceAccount? EnvironmentAccount(RegisteredSourceAdapter adapter, Uri address, string connectionId)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SourceCredentialBroker.EnvironmentVariableFor(connectionId)))) return null;
        var method = adapter.Descriptor.AuthMethods.OfType<ApiTokenAuth>().FirstOrDefault() ?? adapter.Descriptor.AuthMethods[0];
        return new SourceAccount { Id = "env-" + connectionId, Adapter = adapter.Descriptor.Id, BaseUrl = address, AuthMethod = method.Id, DisplayName = "token from the environment" };
    }

    private async Task ConnectQuietlyAsync(Entry entry, CancellationToken cancellationToken)
    {
        try
        {
            await ConnectAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceException)
        {
            // The state says what went wrong; opening the project goes on.
        }
    }

    private async Task ConnectAsync(Entry entry, CancellationToken cancellationToken)
    {
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (entry.Status.State == SourceConnectionState.Connected) return;
            if (entry.Status.State is SourceConnectionState.Failed or SourceConnectionState.SignInRequired) Resolve(entry);
            switch (entry.Status.State)
            {
                case SourceConnectionState.Unbound or SourceConnectionState.AdapterUnavailable or SourceConnectionState.ChooseAccount or SourceConnectionState.Failed:
                    throw new SourceException(new SourceError(SourceErrorKind.Unsupported, entry.Status.Message));
                case SourceConnectionState.SignInRequired:
                    throw new SourceException(new SourceError(SourceErrorKind.Authentication, entry.Status.Message));
            }

            SetStatus(entry, SourceConnectionState.Connecting, string.Empty, entry.Account?.Id);
            try
            {
                await OpenScopeAsync(entry, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(entry.Settings.Adapter)) await OpenSessionAsync(entry, cancellationToken).ConfigureAwait(false);
                SetStatus(entry, SourceConnectionState.Connected, string.Empty, entry.Account?.Id);
            }
            catch (SourceException ex)
            {
                await TearDownAsync(entry).ConfigureAwait(false);
                SetStatus(entry, ex.Error.Kind == SourceErrorKind.Authentication ? SourceConnectionState.SignInRequired : SourceConnectionState.Failed,
                    ex.Error.Message, entry.Account?.Id);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Connection {Connection} could not open.", entry.Settings.Id);
                await TearDownAsync(entry).ConfigureAwait(false);
                SetStatus(entry, SourceConnectionState.Failed, ex.Message, entry.Account?.Id);
                throw new SourceException(new SourceError(SourceErrorKind.Internal, ex.Message), ex);
            }
            catch (OperationCanceledException)
            {
                await TearDownAsync(entry).ConfigureAwait(false);
                Resolve(entry);
                throw;
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private async Task OpenScopeAsync(Entry entry, CancellationToken cancellationToken)
    {
        if (_activation is null || entry.OwnerPluginId is null || WorkspaceId is null) return;
        var results = await _activation.OpenConnectionAsync(WorkspaceId, entry.Settings.Id, entry.OwnerPluginId, cancellationToken).ConfigureAwait(false);
        entry.ScopeOpen = true;
        if (results.FirstOrDefault(r => r.Status == ActivationStatus.Failed) is { } failed)
            throw new SourceException(new SourceError(SourceErrorKind.Internal, failed.Error ?? "The plugin failed to activate the connection."));
    }

    private async Task OpenSessionAsync(Entry entry, CancellationToken cancellationToken)
    {
        var adapter = _adapters!.Find(entry.Settings.Adapter!)!;
        var account = entry.Account!;
        var address = SourceSignInService.ResolveBaseUrl(adapter, entry.Settings.BaseUrl);
        var method = adapter.Descriptor.AuthMethods.FirstOrDefault(m => m.Id == account.AuthMethod);
        CancellationTokenSource scope;
        lock (_gate) scope = CancellationTokenSource.CreateLinkedTokenSource(_workspaceScope.Token);
        entry.Scope = scope;
        var credentials = _signIn!.CredentialsFor(account, entry.Settings.Id);
        entry.Http = _signIn.HttpFor(adapter, address, credentials, method, scope: scope.Token);
        var context = new HostSessionContext(account, address, credentials, entry.Http, _signIn.DiagnosticsFor(adapter), _logger,
            WorkspaceId!, ToInfo(entry.Settings, adapter, address), scope.Token);
        entry.Session = await adapter.Adapter.OpenSessionAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task TearDownAsync(Entry entry)
    {
        if (entry.Scope is { } scope)
        {
            await scope.CancelAsync().ConfigureAwait(false);
        }
        if (entry.Session is { } session)
        {
            entry.Session = null;
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "The session of connection {Connection} failed to close.", entry.Settings.Id); }
        }
        entry.Http?.Dispose();
        entry.Http = null;
        entry.Scope?.Dispose();
        entry.Scope = null;
        if (entry.ScopeOpen && _activation is not null && WorkspaceId is not null)
        {
            entry.ScopeOpen = false;
            await _activation.CloseConnectionAsync(WorkspaceId, entry.Settings.Id).ConfigureAwait(false);
        }
    }

    private static SourceConnectionInfo ToInfo(SourceConnectionSettings settings, RegisteredSourceAdapter adapter, Uri address) => new()
    {
        ConnectionId = settings.Id,
        AdapterId = adapter.Descriptor.Id,
        BaseUrl = address,
        RemoteProjectId = settings.RemoteProject?.Id ?? string.Empty,
        Role = settings.Role == SourceRoles.Primary ? SourceRole.Primary : SourceRole.Mirror,
        Languages = new Dictionary<string, string>(settings.Languages, StringComparer.Ordinal),
        Resources = [.. settings.Resources.Select(r => new SourceResourceMapping(r.Remote, r.Package))],
        Settings = settings.Settings.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal),
    };

    private void SetStatus(Entry entry, SourceConnectionState state, string message, string? accountId = null)
    {
        var status = new SourceConnectionStatus(entry.Settings.Id, state, message, accountId);
        if (entry.Status == status) return;
        entry.Status = status;
        StatusChanged?.Invoke(this, new SourceConnectionStatusChangedEventArgs(status));
    }

    private sealed record HostSessionContext(SourceAccount AccountRecord, Uri BaseUrl, ISourceCredentials Credentials, ISourceHttpClientFactory Http,
        IPluginDiagnostics Diagnostics, ILogger Logger, string WorkspaceId, SourceConnectionInfo Connection, CancellationToken Scope) : ISourceSessionContext
    {
        public string AccountId => AccountRecord.Id;
        public TimeProvider Time => TimeProvider.System;
        public string ConnectionId => Connection.ConnectionId;
    }
}
