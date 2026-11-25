using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>Opens a page in the user's browser. The desktop app opens the system browser; the CLI prints the address.</summary>
public interface IBrowserLauncher
{
    Task OpenAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>What the user needs to finish a device-code sign-in on another device.</summary>
public sealed record DeviceCodePrompt(string UserCode, Uri VerificationUri, Uri? VerificationUriComplete, DateTimeOffset ExpiresAt);

/// <summary>OAuth 2.0 token-endpoint calls shared by the sign-in flows and refresh (RFC 6749, 7636, 8628, 7009).</summary>
public static class SourceOAuth
{
    public const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    /// <summary>A PKCE verifier and its S256 challenge.</summary>
    public static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = ToWebSafeBase64(RandomNumberGenerator.GetBytes(32));
        return (verifier, ToWebSafeBase64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string ToWebSafeBase64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Posts a form to a token endpoint and reads a token response. Throws <see cref="SourceException"/> for an OAuth error.</summary>
    public static async Task<StoredSourceCredential> RequestTokenAsync(HttpClient http, Uri tokenUrl, IEnumerable<KeyValuePair<string, string>> form,
        string methodId, string? previousRefreshToken, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(time);
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(tokenUrl, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var json = Parse(body);
        if (!response.IsSuccessStatusCode || json?.RootElement.TryGetProperty("error", out _) == true)
            throw new SourceException(ErrorFrom(json, response));
        var root = json!.RootElement;
        var access = Text(root, "access_token") ?? throw new SourceException(new SourceError(SourceErrorKind.Internal, "The token response had no access token."));
        var type = Text(root, "token_type") is { Length: > 0 } t && !t.Equals("bearer", StringComparison.OrdinalIgnoreCase) ? t : "Bearer";
        DateTimeOffset? expires = root.TryGetProperty("expires_in", out var e) && e.TryGetInt64(out var seconds) ? time.GetUtcNow().AddSeconds(seconds) : null;
        return new StoredSourceCredential(methodId, type, access, Text(root, "refresh_token") ?? previousRefreshToken, expires);
    }

    /// <summary>The OAuth error in a failed response, as a <see cref="SourceError"/>.</summary>
    public static SourceError ErrorFrom(JsonDocument? json, HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var code = json is null ? null : Text(json.RootElement, "error");
        var description = json is null ? null : Text(json.RootElement, "error_description");
        var kind = code switch
        {
            "invalid_grant" or "invalid_token" or "expired_token" => SourceErrorKind.Authentication,
            "access_denied" or "unauthorized_client" or "invalid_scope" => SourceErrorKind.Authorization,
            "invalid_request" or "invalid_client" or "unsupported_grant_type" => SourceErrorKind.Validation,
            "slow_down" => SourceErrorKind.RateLimited,
            "temporarily_unavailable" => SourceErrorKind.Unavailable,
            null => SourceHttpErrors.FromResponse(response).Kind,
            _ => SourceErrorKind.Unknown,
        };
        return new SourceError(kind, description ?? code ?? $"The sign-in service answered {(int)response.StatusCode}.") { RemoteCode = code };
    }

    internal static JsonDocument? Parse(string body)
    {
        try { return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body); }
        catch (JsonException) { return null; }
    }

    internal static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Authorization code with PKCE: listens on a random loopback port, opens the provider's page in the browser, checks
    /// the returned state and exchanges the code. Times out when the user never finishes.
    /// </summary>
    public static async Task<StoredSourceCredential> AuthorizationCodeAsync(OAuthAuthorizationCodeAuth method, HttpClient http, IBrowserLauncher browser,
        TimeProvider time, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(browser);
        var (verifier, challenge) = CreatePkce();
        var state = ToWebSafeBase64(RandomNumberGenerator.GetBytes(16));
        var port = FreeLoopbackPort();
        var redirect = new Uri($"http://127.0.0.1:{port}/callback");

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        await using var stop = limit.Token.Register(listener.Stop).ConfigureAwait(false);

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = method.ClientId,
            ["redirect_uri"] = redirect.ToString(),
            ["scope"] = string.Join(' ', method.Scopes),
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };
        await browser.OpenAsync(WithQuery(method.AuthorizeUrl, query), limit.Token).ConfigureAwait(false);

        string code;
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException && limit.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new SourceException(new SourceError(SourceErrorKind.Timeout, "Sign-in was not finished in the browser in time."));
            }
            using var response = context.Response;
            if (context.Request.Url?.AbsolutePath != "/callback") { Reply(response, 404, "Not found."); continue; }
            var parameters = ParseQuery(context.Request.Url.Query);
            if (parameters.GetValueOrDefault("state") != state) { Reply(response, 400, "This sign-in link is not the one Toucan started."); continue; }
            if (parameters.TryGetValue("error", out var error))
            {
                Reply(response, 200, "Sign-in was cancelled. You can close this window.");
                throw new SourceException(new SourceError(error == "access_denied" ? SourceErrorKind.Authorization : SourceErrorKind.Authentication,
                    $"Sign-in was declined: {parameters.GetValueOrDefault("error_description") ?? error}.") { RemoteCode = error });
            }
            if (!parameters.TryGetValue("code", out var received) || received.Length == 0) { Reply(response, 400, "The sign-in answer had no code."); continue; }
            Reply(response, 200, "Signed in to Toucan. You can close this window.");
            code = received;
            break;
        }

        return await RequestTokenAsync(http, method.TokenUrl,
        [
            new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", redirect.ToString()),
            new("client_id", method.ClientId), new("code_verifier", verifier),
        ], method.Id, null, time, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Device authorization: shows a code for the user to enter elsewhere and polls until they approve, decline or the code expires.</summary>
    public static async Task<StoredSourceCredential> DeviceCodeAsync(OAuthDeviceCodeAuth method, HttpClient http, SourceNetworkPolicy policy,
        Func<DeviceCodePrompt, Task> prompt, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(time);
        using var request = new FormUrlEncodedContent([new("client_id", method.ClientId), new("scope", string.Join(' ', method.Scopes))]);
        using var response = await http.PostAsync(method.DeviceAuthorizationUrl, request, cancellationToken).ConfigureAwait(false);
        using var json = Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!response.IsSuccessStatusCode || json is null) throw new SourceException(ErrorFrom(json, response));
        var root = json.RootElement;
        var deviceCode = Text(root, "device_code");
        var userCode = Text(root, "user_code");
        var verification = Text(root, "verification_uri") ?? Text(root, "verification_url");
        if (deviceCode is null || userCode is null || !Uri.TryCreate(verification, UriKind.Absolute, out var verificationUri))
            throw new SourceException(new SourceError(SourceErrorKind.Internal, "The device sign-in answer was incomplete."));
        // Only show the user an address on a host the plugin declared, so a compromised answer cannot send them elsewhere.
        if (!policy.Allows(verificationUri, out var why))
            throw new SourceException(new SourceError(SourceErrorKind.Unsupported, $"The sign-in page {verificationUri.Host} is not allowed: {why}."));
        Uri? complete = Uri.TryCreate(Text(root, "verification_uri_complete"), UriKind.Absolute, out var c) && policy.Allows(c, out _) ? c : null;
        var lifetime = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? TimeSpan.FromSeconds(s) : TimeSpan.FromMinutes(10);
        var interval = root.TryGetProperty("interval", out var i) && i.TryGetInt32(out var n) ? TimeSpan.FromSeconds(n) : TimeSpan.FromSeconds(5);
        var expires = time.GetUtcNow() + lifetime;
        await prompt(new DeviceCodePrompt(userCode, verificationUri, complete, expires)).ConfigureAwait(false);

        while (true)
        {
            if (time.GetUtcNow() >= expires) throw new SourceException(new SourceError(SourceErrorKind.Authentication, "The sign-in code expired. Start again."));
            if (interval > TimeSpan.Zero) await Task.Delay(interval, time, cancellationToken).ConfigureAwait(false);
            try
            {
                return await RequestTokenAsync(http, method.TokenUrl,
                    [new("grant_type", DeviceGrant), new("device_code", deviceCode), new("client_id", method.ClientId)],
                    method.Id, null, time, cancellationToken).ConfigureAwait(false);
            }
            catch (SourceException ex) when (ex.Error.RemoteCode == "authorization_pending")
            {
            }
            catch (SourceException ex) when (ex.Error.RemoteCode == "slow_down")
            {
                interval += TimeSpan.FromSeconds(5);
            }
            catch (SourceException ex) when (ex.Error.RemoteCode == "access_denied")
            {
                throw new SourceException(new SourceError(SourceErrorKind.Authorization, "Sign-in was declined."), ex);
            }
            catch (SourceException ex) when (ex.Error.RemoteCode == "expired_token")
            {
                throw new SourceException(new SourceError(SourceErrorKind.Authentication, "The sign-in code expired. Start again."), ex);
            }
        }
    }

    /// <summary>Asks the provider to revoke a token (RFC 7009). Best effort: failures are ignored, the local credential is removed anyway.</summary>
    public static async Task<bool> RevokeAsync(HttpClient http, Uri revokeUrl, string clientId, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        try
        {
            using var content = new FormUrlEncodedContent([new("token", token), new("client_id", clientId)]);
            using var response = await http.PostAsync(revokeUrl, content, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or SourceException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static int FreeLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static Uri WithQuery(Uri url, IReadOnlyDictionary<string, string> query)
    {
        var builder = new UriBuilder(url);
        var existing = builder.Query.TrimStart('?');
        var added = string.Join('&', query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        builder.Query = existing.Length > 0 ? existing + "&" + added : added;
        return builder.Uri;
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            result[Uri.UnescapeDataString(pair[0].Replace('+', ' '))] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
        }
        return result;
    }

    private static void Reply(HttpListenerResponse response, int status, string message)
    {
        var html = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>Toucan</title><p style=\"font-family:system-ui\">{WebUtility.HtmlEncode(message)}</p>");
        response.StatusCode = status;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = html.Length;
        response.OutputStream.Write(html);
    }
}
