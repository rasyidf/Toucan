using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>What the secret store keeps for an account. Adapters only ever see <see cref="Value"/>.</summary>
/// <param name="Method">The sign-in method ID that produced it.</param>
/// <param name="Scheme">How it is sent: <c>Bearer</c>, <c>Basic</c>, or the token method's scheme.</param>
public sealed record StoredSourceCredential(string Method, string Scheme, string Value, string? RefreshToken = null, DateTimeOffset? ExpiresAt = null)
{
    public SourceCredential ToCredential() => new(Scheme, Value, ExpiresAt);

    /// <summary>Never prints secrets.</summary>
    public override string ToString() => $"{Method} credential{(ExpiresAt is { } at ? $" until {at:u}" : string.Empty)}";
}

/// <summary>Renews an expiring credential, for sign-in methods that can (OAuth refresh tokens, custom exchanges).</summary>
public interface ISourceTokenRefresher
{
    Task<StoredSourceCredential> RefreshAsync(SourceAccount account, StoredSourceCredential current, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps each account's credential in the secret store, registers it for redaction, hands sessions a current one and
/// refreshes it once for everyone: across threads, and across the desktop app and the CLI through a lock file next to the
/// secret store. Adapters never see refresh tokens.
/// </summary>
public sealed class SourceCredentialBroker
{
    /// <summary>A credential this close to expiry is refreshed before use.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private readonly ISecretService _secrets;
    private readonly IDiagnosticsService _diagnostics;
    private readonly string _lockFolder;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public SourceCredentialBroker(ISecretService secrets, IDiagnosticsService diagnostics, string? lockFolder = null, TimeProvider? time = null)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _lockFolder = lockFolder ?? Path.Combine(SecureStorageService.DefaultFolder, "locks");
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The environment variable that supplies a connection's token in CI, e.g. <c>TOUCAN_SOURCE_TOKEN_CON_1A2B</c>.</summary>
    public static string EnvironmentVariableFor(string connectionId)
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        var name = new StringBuilder("TOUCAN_SOURCE_TOKEN_");
        foreach (var c in connectionId.ToUpperInvariant()) name.Append(c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? c : '_');
        return name.ToString();
    }

    public StoredSourceCredential? Read(SourceAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var json = _secrets.GetSecret(account.CredentialKey);
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredSourceCredential>(json, s_json);
            if (stored is null || string.IsNullOrEmpty(stored.Value)) return null;
            Register(stored);
            return stored;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Store(SourceAccount account, StoredSourceCredential credential)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(credential);
        Register(credential);
        _secrets.SetSecret(account.CredentialKey, JsonSerializer.Serialize(credential, s_json));
    }

    /// <summary>Removes the stored credential. Returns false when there was none.</summary>
    public bool Forget(SourceAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _secrets.Remove(account.CredentialKey);
    }

    /// <summary>
    /// The credential source for a session of <paramref name="connectionId"/> through <paramref name="account"/>. A token in
    /// <see cref="EnvironmentVariableFor"/> wins and is never stored; the scheme comes from <paramref name="environmentScheme"/>.
    /// </summary>
    public ISourceCredentials For(SourceAccount account, string? connectionId = null, ISourceTokenRefresher? refresher = null, string environmentScheme = "Bearer")
    {
        ArgumentNullException.ThrowIfNull(account);
        var fromEnvironment = connectionId is null ? null : Environment.GetEnvironmentVariable(EnvironmentVariableFor(connectionId));
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            _diagnostics.RegisterSecret(fromEnvironment);
            return new FixedCredentials(new SourceCredential(environmentScheme, fromEnvironment));
        }
        return new AccountCredentials(this, account, refresher);
    }

    private void Register(StoredSourceCredential credential)
    {
        _diagnostics.RegisterSecret(credential.Value);
        if (credential.RefreshToken is { Length: > 0 } refresh) _diagnostics.RegisterSecret(refresh);
    }

    private SemaphoreSlim GateFor(string accountId)
    {
        lock (_gates)
        {
            if (!_gates.TryGetValue(accountId, out var gate)) _gates[accountId] = gate = new SemaphoreSlim(1, 1);
            return gate;
        }
    }

    private static SourceException SignInAgain(SourceAccount account, string why) =>
        new(new SourceError(SourceErrorKind.Authentication, $"{why} Sign in to {account.DisplayName} again."));

    /// <summary>Holds the account's lock file so another process does not refresh at the same time.</summary>
    private async Task<IDisposable> LockAcrossProcessesAsync(string accountId, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_lockFolder);
        var path = Path.Combine(_lockFolder, $"source-{accountId}.lock");
        var deadline = _time.GetUtcNow() + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (_time.GetUtcNow() < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), _time, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class FixedCredentials(SourceCredential credential) : ISourceCredentials
    {
        public Task<SourceCredential> GetAsync(CancellationToken cancellationToken) => Task.FromResult(credential);

        public Task<SourceCredential> RefreshAsync(SourceCredential rejected, CancellationToken cancellationToken) =>
            throw new SourceException(new SourceError(SourceErrorKind.Authentication, "The token from the environment was rejected."));
    }

    private sealed class AccountCredentials(SourceCredentialBroker broker, SourceAccount account, ISourceTokenRefresher? refresher) : ISourceCredentials
    {
        public async Task<SourceCredential> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stored = broker.Read(account) ?? throw SignInAgain(account, "You are not signed in.");
            if (stored.ExpiresAt is { } expires && expires - broker._time.GetUtcNow() <= RefreshMargin && refresher is not null && stored.RefreshToken is not null)
                return await RefreshAsync(stored.ToCredential(), cancellationToken).ConfigureAwait(false);
            return stored.ToCredential();
        }

        public async Task<SourceCredential> RefreshAsync(SourceCredential rejected, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(rejected);
            var gate = broker.GateFor(account.Id);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var fileLock = await broker.LockAcrossProcessesAsync(account.Id, cancellationToken).ConfigureAwait(false);
                var stored = broker.Read(account) ?? throw SignInAgain(account, "You are not signed in.");
                // Someone (another thread or process) already replaced the rejected credential: use theirs.
                if (stored.Value != rejected.Value && !NearExpiry(stored)) return stored.ToCredential();
                if (refresher is null || stored.RefreshToken is null) throw SignInAgain(account, "Your sign-in has expired.");
                StoredSourceCredential renewed;
                try
                {
                    renewed = await refresher.RefreshAsync(account, stored, cancellationToken).ConfigureAwait(false);
                }
                catch (SourceException ex) when (ex.Error.Kind is SourceErrorKind.Authentication or SourceErrorKind.Authorization or SourceErrorKind.Validation)
                {
                    throw SignInAgain(account, "Your sign-in could not be renewed.");
                }
                broker.Store(account, renewed);
                return renewed.ToCredential();
            }
            finally
            {
                gate.Release();
            }
        }

        private bool NearExpiry(StoredSourceCredential stored) => stored.ExpiresAt is { } at && at - broker._time.GetUtcNow() <= RefreshMargin;
    }
}
