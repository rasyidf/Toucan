namespace Toucan.Plugins.Sources;

/// <summary>
/// A way to sign in. The host runs every flow, stores the result in the secret store and hands sessions only a current
/// credential (<see cref="ISourceCredentials"/>); adapters never see refresh tokens or stored passwords.
/// </summary>
/// <param name="Id">Stable within the adapter, e.g. <c>token</c> or <c>oauth</c>.</param>
/// <param name="Label">Shown when the user chooses how to sign in.</param>
public abstract record SourceAuthMethod(string Id, string Label);

/// <summary>A personal access token or API key the user pastes in.</summary>
/// <param name="HeaderName">Header the host's HTTP client puts it in.</param>
/// <param name="Scheme">Prefix before the token in the header, or null for the raw token.</param>
/// <param name="TokenHelpUrl">Page where the user creates a token.</param>
public sealed record ApiTokenAuth(string Id, string Label, string HeaderName = "Authorization", string? Scheme = "Bearer", Uri? TokenHelpUrl = null)
    : SourceAuthMethod(Id, Label);

/// <summary>A user name and password sent as HTTP basic authentication.</summary>
public sealed record BasicAuth(string Id, string Label) : SourceAuthMethod(Id, Label);

/// <summary>
/// OAuth 2.0 authorization code with PKCE, through the system browser and a loopback redirect. Desktop only. A distributed
/// plugin cannot keep a client secret, so there is none.
/// </summary>
public sealed record OAuthAuthorizationCodeAuth(string Id, string Label, Uri AuthorizeUrl, Uri TokenUrl, string ClientId,
    IReadOnlyList<string> Scopes, Uri? RevokeUrl = null) : SourceAuthMethod(Id, Label);

/// <summary>OAuth 2.0 device authorization: the user enters a code on another device. Works in the CLI.</summary>
public sealed record OAuthDeviceCodeAuth(string Id, string Label, Uri DeviceAuthorizationUrl, Uri TokenUrl, string ClientId,
    IReadOnlyList<string> Scopes, Uri? RevokeUrl = null) : SourceAuthMethod(Id, Label);

/// <summary>
/// A form the host shows; the adapter turns what the user typed into a credential with <see cref="ISourceAuthExchange"/>.
/// For services with their own login. Secret fields are passed once and never stored.
/// </summary>
public sealed record CustomAuth(string Id, string Label, ConfigSchema Fields) : SourceAuthMethod(Id, Label);

/// <summary>
/// The service needs no sign-in (a folder on this computer). The host opens such connections without an account and gives
/// the session no credential.
/// </summary>
public sealed record NoSignInAuth(string Id = "none", string Label = "No sign-in needed") : SourceAuthMethod(Id, Label);

/// <summary>A credential ready to use. <paramref name="Value"/> is secret: the host registers it for redaction.</summary>
/// <param name="Scheme">How it is sent, e.g. <c>Bearer</c>, <c>Basic</c>, or an adapter-defined name.</param>
public sealed record SourceCredential(string Scheme, string Value, DateTimeOffset? ExpiresAt = null)
{
    /// <summary>Never prints the value.</summary>
    public override string ToString() => $"{Scheme} credential{(ExpiresAt is { } at ? $" until {at:u}" : string.Empty)}";
}

/// <summary>The current credential of the account behind a session.</summary>
public interface ISourceCredentials
{
    /// <summary>A valid credential, refreshed first when it is close to expiry. Throws <see cref="SourceException"/> (Authentication) when the user must sign in again.</summary>
    Task<SourceCredential> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Call after the service rejected <paramref name="rejected"/> as expired. Refreshes once; concurrent callers share the
    /// refresh. Throws <see cref="SourceException"/> (Authentication) when it cannot.
    /// </summary>
    Task<SourceCredential> RefreshAsync(SourceCredential rejected, CancellationToken cancellationToken);
}

/// <summary>Implemented by an adapter that declares a <see cref="CustomAuth"/> method.</summary>
public interface ISourceAuthExchange
{
    /// <summary>Turns the fields the user typed into a credential. Use <paramref name="http"/>; the host checks its hosts.</summary>
    Task<SourceCredential> ExchangeAsync(string methodId, IReadOnlyDictionary<string, string> fields, ISourceHttpClientFactory http, CancellationToken cancellationToken);

    /// <summary>Renews an expiring credential. The default says it cannot, so the user signs in again.</summary>
    Task<SourceCredential> RefreshAsync(string methodId, SourceCredential current, ISourceHttpClientFactory http, CancellationToken cancellationToken) =>
        throw new SourceException(new SourceError(SourceErrorKind.Authentication, "This sign-in cannot be renewed; sign in again."));
}

/// <summary>
/// HTTP clients from the host: requests go only to the manifest's hosts and the account's address, over HTTPS (loopback
/// excepted), with the credential attached, rate limits and safe retries applied, and secrets redacted from diagnostics.
/// </summary>
public interface ISourceHttpClientFactory
{
    /// <summary>A client for this account. Dispose it when done; the handlers behind it are shared.</summary>
    HttpClient Create();
}
