using System.Text;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>
/// Signs users in to content sources and keeps their accounts: runs each kind of sign-in for an adapter, stores the
/// credential through the broker, renews it, signs out (revoking where the provider allows), and builds the HTTP clients
/// adapters use for an account.
/// </summary>
public sealed class SourceSignInService
{
    private readonly ISourceAccountStore _accounts;
    private readonly SourceCredentialBroker _broker;
    private readonly ISourceAdapterCatalog _adapters;
    private readonly IPluginCatalog? _plugins;
    private readonly IDiagnosticsService _diagnostics;
    private readonly TimeProvider _time;
    private readonly SourceHttpOptions _httpOptions;

    public SourceSignInService(ISourceAccountStore accounts, SourceCredentialBroker broker, ISourceAdapterCatalog adapters, IDiagnosticsService diagnostics,
        IPluginCatalog? plugins = null, TimeProvider? time = null, SourceHttpOptions? httpOptions = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _plugins = plugins;
        _time = time ?? TimeProvider.System;
        _httpOptions = httpOptions ?? new SourceHttpOptions { UserAgent = $"Toucan/{typeof(SourceSignInService).Assembly.GetName().Version?.ToString(3)}" };
    }

    /// <summary>How long a browser sign-in may take before it is abandoned.</summary>
    public TimeSpan BrowserTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public ISourceAccountStore Accounts => _accounts;
    public SourceCredentialBroker Broker => _broker;

    public RegisteredSourceAdapter Adapter(string adapterId) =>
        _adapters.Find(adapterId) ?? throw new InvalidOperationException($"No loaded plugin provides the source adapter '{adapterId}'. Is the plugin installed, trusted and enabled?");

    /// <summary>The address an account for <paramref name="adapter"/> uses: the requested one when the adapter allows custom addresses, else its default.</summary>
    public static Uri ResolveBaseUrl(RegisteredSourceAdapter adapter, Uri? requested)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var d = adapter.Descriptor;
        if (requested is null) return d.DefaultBaseUrl ?? throw new InvalidOperationException($"{d.DisplayName} needs the address of your server.");
        if (!SourceAdapterRules.IsSecure(requested)) throw new InvalidOperationException($"{requested} must use HTTPS.");
        if (d.AllowsCustomBaseUrl || (d.DefaultBaseUrl is { } fixedUrl && SourceAccounts.SameInstance(fixedUrl, requested))) return requested;
        throw new InvalidOperationException($"{d.DisplayName} only connects to {d.DefaultBaseUrl}.");
    }

    /// <summary>The plugin's declared hosts plus <paramref name="address"/>. Built-in adapters may reach only the address.</summary>
    public SourceNetworkPolicy PolicyFor(RegisteredSourceAdapter adapter, Uri address)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var manifest = adapter.IsBuiltIn ? null : _plugins?.Plugins.FirstOrDefault(p => p.Manifest?.Id == adapter.PluginId)?.Manifest;
        // A plugin may only use the account address it allows: its declared hosts, or any host when it accepts user-defined ones.
        var network = manifest?.Network;
        var allowAddress = adapter.IsBuiltIn || network?.UserDefinedHosts == true || network?.Allows(address.Host) == true || _plugins is null;
        return new SourceNetworkPolicy(network, allowAddress ? address : null);
    }

    public IPluginDiagnostics DiagnosticsFor(RegisteredSourceAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return new PluginDiagnostics(adapter.PluginId, _diagnostics);
    }

    /// <summary>HTTP clients for an account; <paramref name="credentials"/> null sends none (sign-in and token endpoints).</summary>
    public SourceHttpClientFactory HttpFor(RegisteredSourceAdapter adapter, Uri address, ISourceCredentials? credentials, SourceAuthMethod? method,
        SourceLimits? limits = null, CancellationToken scope = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var options = _httpOptions with
        {
            MaxConcurrency = limits?.MaxConcurrency ?? _httpOptions.MaxConcurrency,
            RequestsPerMinute = limits?.RequestsPerMinute ?? _httpOptions.RequestsPerMinute,
            UserAgent = $"{_httpOptions.UserAgent} {adapter.PluginId}",
        };
        return new SourceHttpClientFactory(PolicyFor(adapter, address), credentials, method, DiagnosticsFor(adapter), options, _time, scope);
    }

    public SourceAuthMethod Method(RegisteredSourceAdapter adapter, string methodId)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return adapter.Descriptor.AuthMethods.FirstOrDefault(m => m.Id == methodId)
               ?? throw new InvalidOperationException($"{adapter.Descriptor.DisplayName} has no sign-in method '{methodId}'.");
    }

    /// <summary>The account's credential source for a session, renewing it when the sign-in method can.</summary>
    public ISourceCredentials CredentialsFor(SourceAccount account, string? connectionId = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        var adapter = _adapters.Find(account.Adapter);
        var method = adapter?.Descriptor.AuthMethods.FirstOrDefault(m => m.Id == account.AuthMethod);
        var scheme = method switch { ApiTokenAuth t => t.Scheme ?? string.Empty, BasicAuth => "Basic", _ => "Bearer" };
        return _broker.For(account, connectionId, adapter is null ? null : RefresherFor(adapter, account, method), scheme);
    }

    // ───────────────────────────── sign-in ─────────────────────────────

    /// <summary>Stores a pasted access token. <paramref name="existing"/> renews an account instead of adding one.</summary>
    public Task<SourceAccount> SignInWithTokenAsync(string adapterId, Uri? baseUrl, string methodId, string token, string? displayName = null, SourceAccount? existing = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var adapter = Adapter(adapterId);
        if (Method(adapter, methodId) is not ApiTokenAuth method) throw new InvalidOperationException($"'{methodId}' is not a token sign-in.");
        return Task.FromResult(Save(adapter, baseUrl, method.Id, displayName, existing, new StoredSourceCredential(method.Id, method.Scheme ?? string.Empty, token.Trim())));
    }

    public Task<SourceAccount> SignInWithPasswordAsync(string adapterId, Uri? baseUrl, string methodId, string user, string password, string? displayName = null, SourceAccount? existing = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrEmpty(password);
        var adapter = Adapter(adapterId);
        if (Method(adapter, methodId) is not BasicAuth method) throw new InvalidOperationException($"'{methodId}' is not a user name and password sign-in.");
        var value = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        return Task.FromResult(Save(adapter, baseUrl, method.Id, displayName ?? user, existing, new StoredSourceCredential(method.Id, "Basic", value)));
    }

    public async Task<SourceAccount> SignInWithBrowserAsync(string adapterId, Uri? baseUrl, string methodId, IBrowserLauncher browser,
        string? displayName = null, SourceAccount? existing = null, CancellationToken cancellationToken = default)
    {
        var adapter = Adapter(adapterId);
        if (Method(adapter, methodId) is not OAuthAuthorizationCodeAuth method) throw new InvalidOperationException($"'{methodId}' is not a browser sign-in.");
        var address = ResolveBaseUrl(adapter, baseUrl);
        using var http = HttpFor(adapter, address, null, null, scope: cancellationToken);
        using var client = http.Create();
        var credential = await SourceOAuth.AuthorizationCodeAsync(method, client, browser, _time, BrowserTimeout, cancellationToken).ConfigureAwait(false);
        return Save(adapter, address, method.Id, displayName, existing, credential);
    }

    public async Task<SourceAccount> SignInWithDeviceCodeAsync(string adapterId, Uri? baseUrl, string methodId, Func<DeviceCodePrompt, Task> prompt,
        string? displayName = null, SourceAccount? existing = null, CancellationToken cancellationToken = default)
    {
        var adapter = Adapter(adapterId);
        if (Method(adapter, methodId) is not OAuthDeviceCodeAuth method) throw new InvalidOperationException($"'{methodId}' is not a device-code sign-in.");
        var address = ResolveBaseUrl(adapter, baseUrl);
        using var http = HttpFor(adapter, address, null, null, scope: cancellationToken);
        using var client = http.Create();
        var credential = await SourceOAuth.DeviceCodeAsync(method, client, PolicyFor(adapter, address), prompt, _time, cancellationToken).ConfigureAwait(false);
        return Save(adapter, address, method.Id, displayName, existing, credential);
    }

    /// <summary>A custom sign-in: the adapter turns the form's fields into a credential. Secret fields are not stored.</summary>
    public async Task<SourceAccount> SignInWithFormAsync(string adapterId, Uri? baseUrl, string methodId, IReadOnlyDictionary<string, string> fields,
        string? displayName = null, SourceAccount? existing = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var adapter = Adapter(adapterId);
        if (Method(adapter, methodId) is not CustomAuth method) throw new InvalidOperationException($"'{methodId}' is not a custom sign-in.");
        if (adapter.Adapter is not ISourceAuthExchange exchange) throw new InvalidOperationException($"{adapter.Descriptor.DisplayName} cannot exchange sign-in details.");
        foreach (var field in method.Fields.Fields.Where(f => f.Type == ConfigFieldType.Secret && fields.TryGetValue(f.Key, out _)))
            _diagnostics.RegisterSecret(fields[field.Key]);
        var address = ResolveBaseUrl(adapter, baseUrl);
        using var http = HttpFor(adapter, address, null, null, scope: cancellationToken);
        var credential = await exchange.ExchangeAsync(method.Id, fields, http, cancellationToken).ConfigureAwait(false);
        return Save(adapter, address, method.Id, displayName, existing,
            new StoredSourceCredential(method.Id, credential.Scheme, credential.Value, null, credential.ExpiresAt));
    }

    /// <summary>Revokes the token where the provider supports it, forgets the credential and removes the account.</summary>
    public async Task<bool> SignOutAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var account = _accounts.Find(accountId);
        if (account is null) return false;
        var stored = _broker.Read(account);
        if (stored is not null && _adapters.Find(account.Adapter) is { } adapter)
        {
            var (revoke, clientId) = adapter.Descriptor.AuthMethods.FirstOrDefault(m => m.Id == account.AuthMethod) switch
            {
                OAuthAuthorizationCodeAuth code => (code.RevokeUrl, code.ClientId),
                OAuthDeviceCodeAuth device => (device.RevokeUrl, device.ClientId),
                _ => (null, string.Empty),
            };
            if (revoke is not null)
            {
                using var http = HttpFor(adapter, account.BaseUrl, null, null, scope: cancellationToken);
                using var client = http.Create();
                await SourceOAuth.RevokeAsync(client, revoke, clientId, stored.RefreshToken ?? stored.Value, cancellationToken).ConfigureAwait(false);
            }
        }
        _broker.Forget(account);
        return _accounts.Remove(account.Id);
    }

    private SourceAccount Save(RegisteredSourceAdapter adapter, Uri? baseUrl, string methodId, string? displayName, SourceAccount? existing, StoredSourceCredential credential)
    {
        var address = ResolveBaseUrl(adapter, baseUrl);
        SourceAccount account;
        if (existing is not null)
        {
            if (!string.Equals(existing.Adapter, adapter.Descriptor.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Account {existing.DisplayName} is for another service.");
            account = existing with { AuthMethod = methodId, BaseUrl = address, DisplayName = displayName ?? existing.DisplayName };
            _accounts.Update(account);
        }
        else
        {
            account = _accounts.Add(adapter.Descriptor.Id, address, methodId, displayName ?? $"{adapter.Descriptor.DisplayName} ({address.Host})");
        }
        _broker.Store(account, credential);
        return account;
    }

    private ISourceTokenRefresher? RefresherFor(RegisteredSourceAdapter adapter, SourceAccount account, SourceAuthMethod? method) => method switch
    {
        OAuthAuthorizationCodeAuth code => new OAuthRefresher(this, adapter, code.TokenUrl, code.ClientId),
        OAuthDeviceCodeAuth device => new OAuthRefresher(this, adapter, device.TokenUrl, device.ClientId),
        CustomAuth custom when adapter.Adapter is ISourceAuthExchange exchange => new ExchangeRefresher(this, adapter, exchange, custom.Id),
        _ => null,
    };

    private sealed class OAuthRefresher(SourceSignInService owner, RegisteredSourceAdapter adapter, Uri tokenUrl, string clientId) : ISourceTokenRefresher
    {
        public async Task<StoredSourceCredential> RefreshAsync(SourceAccount account, StoredSourceCredential current, CancellationToken cancellationToken)
        {
            using var http = owner.HttpFor(adapter, account.BaseUrl, null, null, scope: cancellationToken);
            using var client = http.Create();
            return await SourceOAuth.RequestTokenAsync(client, tokenUrl,
                [new("grant_type", "refresh_token"), new("refresh_token", current.RefreshToken!), new("client_id", clientId)],
                current.Method, current.RefreshToken, owner._time, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ExchangeRefresher(SourceSignInService owner, RegisteredSourceAdapter adapter, ISourceAuthExchange exchange, string methodId) : ISourceTokenRefresher
    {
        public async Task<StoredSourceCredential> RefreshAsync(SourceAccount account, StoredSourceCredential current, CancellationToken cancellationToken)
        {
            using var http = owner.HttpFor(adapter, account.BaseUrl, null, null, scope: cancellationToken);
            var renewed = await exchange.RefreshAsync(methodId, current.ToCredential(), http, cancellationToken).ConfigureAwait(false);
            return current with { Scheme = renewed.Scheme, Value = renewed.Value, ExpiresAt = renewed.ExpiresAt };
        }
    }
}
