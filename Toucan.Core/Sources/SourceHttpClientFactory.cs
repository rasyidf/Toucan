using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Core.Sources;

/// <summary>Where an adapter may send requests: the hosts its manifest declares and the account's own address.</summary>
public sealed class SourceNetworkPolicy(NetworkAccess? network, Uri? accountAddress)
{
    public NetworkAccess? Network { get; } = network;
    public Uri? AccountAddress { get; } = accountAddress;

    /// <summary>HTTPS (or HTTP to this machine) to a declared host or the account's host.</summary>
    public bool Allows(Uri url, out string reason)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri) { reason = "the address is not absolute"; return false; }
        if (!SourceAdapterRules.IsSecure(url)) { reason = $"{url.Scheme} is not allowed; use HTTPS"; return false; }
        if (url.IsLoopback || Network?.Allows(url.Host) == true
            || (AccountAddress is not null && string.Equals(AccountAddress.Host, url.Host, StringComparison.OrdinalIgnoreCase)))
        {
            reason = string.Empty;
            return true;
        }
        reason = $"{url.Host} is not among the hosts the plugin declared";
        return false;
    }
}

/// <summary>Limits and behaviour of the HTTP clients given to adapters.</summary>
public sealed record SourceHttpOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(100);

    /// <summary>Retries after the first attempt, for idempotent requests only.</summary>
    public int MaxRetries { get; init; } = 3;

    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>A service asking to wait longer than this gets the error instead.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxConcurrency { get; init; } = 4;
    public int? RequestsPerMinute { get; init; }
    public int MaxRedirects { get; init; } = 5;

    /// <summary>Writes one redacted line per request (method, host, path, status, time) to the plugin's diagnostics. No bodies or queries.</summary>
    public bool LogRequests { get; init; }

    public string UserAgent { get; init; } = "Toucan";

    /// <summary>The innermost handler; tests pass a stub server. Defaults to a handler that does not follow redirects by itself.</summary>
    public Func<HttpMessageHandler>? PrimaryHandler { get; init; }
}

/// <summary>
/// HTTP clients for one account. Every request passes, from outside in: the connection scope's cancellation; the host
/// allow-list (redirects included, followed here and re-checked); safe retries with backoff that honour <c>Retry-After</c>;
/// a concurrency and per-minute limit shared by the account; the credential, refreshed once on 401; and optional redacted
/// logging. The handler chain is shared, so create clients freely.
/// </summary>
public sealed class SourceHttpClientFactory : ISourceHttpClientFactory, IDisposable
{
    private readonly HttpMessageHandler _chain;
    private readonly SourceHttpOptions _options;

    public SourceHttpClientFactory(SourceNetworkPolicy policy, ISourceCredentials? credentials, SourceAuthMethod? method,
        IPluginDiagnostics diagnostics, SourceHttpOptions? options = null, TimeProvider? time = null, CancellationToken scope = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _options = options ?? new SourceHttpOptions();
        var clock = time ?? TimeProvider.System;
        HttpMessageHandler inner = _options.PrimaryHandler?.Invoke() ?? new SocketsHttpHandler { AllowAutoRedirect = false };
        inner = new LoggingHandler(diagnostics, _options.LogRequests, clock) { InnerHandler = inner };
        if (credentials is not null) inner = new AuthHandler(credentials, method, policy.AccountAddress) { InnerHandler = inner };
        inner = new ThrottleHandler(_options, clock) { InnerHandler = inner };
        inner = new RetryHandler(_options, clock) { InnerHandler = inner };
        inner = new PolicyHandler(policy, _options, diagnostics) { InnerHandler = inner };
        _chain = new ScopeHandler(_options.UserAgent, scope) { InnerHandler = inner };
    }

    public HttpClient Create() => new(_chain, disposeHandler: false) { Timeout = _options.Timeout };

    public void Dispose() => _chain.Dispose();

    /// <summary>A copy of a request that can be sent again (content is buffered first).</summary>
    internal static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, Uri? uri = null, HttpMethod? method = null)
    {
        var copy = new HttpRequestMessage(method ?? request.Method, uri ?? request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options) ((IDictionary<string, object?>)copy.Options)[option.Key] = option.Value;
        if (request.Content is not null && method != HttpMethod.Get)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            copy.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers) copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return copy;
    }

    private static bool IsIdempotent(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options
        || request.Method == HttpMethod.Put || request.Method == HttpMethod.Delete || request.Headers.Contains("Idempotency-Key");

    private sealed class ScopeHandler(string userAgent, CancellationToken scope) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.UserAgent.Count == 0) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(scope, cancellationToken);
            return await base.SendAsync(request, linked.Token).ConfigureAwait(false);
        }
    }

    private sealed class PolicyHandler(SourceNetworkPolicy policy, SourceHttpOptions options, IPluginDiagnostics diagnostics) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Check(request.RequestUri);
            if (request.Content is not null) await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
            var current = request;
            for (var redirects = 0; ; redirects++)
            {
                var response = await base.SendAsync(current, cancellationToken).ConfigureAwait(false);
                if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location || redirects >= options.MaxRedirects) return response;
                var target = location.IsAbsoluteUri ? location : new Uri(current.RequestUri!, location);
                Check(target);
                // 301/302/303 turn into GET without a body, as browsers do; 307/308 repeat the request as it was.
                var method = response.StatusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect || current.Method == HttpMethod.Head
                    ? current.Method : HttpMethod.Get;
                response.Dispose();
                current = await CloneAsync(current, target, method).ConfigureAwait(false);
            }
        }

        private void Check(Uri? url)
        {
            if (url is not null && policy.Allows(url, out _)) return;
            var reason = url is null ? "the request has no address" : policy.Allows(url, out var why) ? string.Empty : why;
            var message = $"Toucan blocked a request to {url?.GetLeftPart(UriPartial.Authority) ?? "(none)"}: {reason}.";
            diagnostics.Write(DiagnosticLevel.Warning, message);
            throw new SourceException(new SourceError(SourceErrorKind.Unsupported, message));
        }

        private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
            or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    }

    private sealed class RetryHandler(SourceHttpOptions options, TimeProvider time) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!IsIdempotent(request)) return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; ; attempt++)
            {
                var send = attempt == 0 ? request : await CloneAsync(request).ConfigureAwait(false);
                HttpResponseMessage response;
                try
                {
                    response = await base.SendAsync(send, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException) when (attempt < options.MaxRetries)
                {
                    await Task.Delay(Backoff(attempt), time, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (attempt >= options.MaxRetries || !IsTransient(response.StatusCode)) return response;
                var wait = RetryAfter(response) ?? Backoff(attempt);
                if (wait > options.MaxRetryDelay) return response;
                response.Dispose();
                await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

        private TimeSpan Backoff(int attempt)
        {
            var exponential = options.BaseRetryDelay.TotalMilliseconds * Math.Pow(2, attempt);
            var jitter = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000) / 1000.0 * options.BaseRetryDelay.TotalMilliseconds;
            return TimeSpan.FromMilliseconds(Math.Min(exponential + jitter, options.MaxRetryDelay.TotalMilliseconds));
        }

        private TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - time.GetUtcNow() is var wait && wait > TimeSpan.Zero ? wait : TimeSpan.Zero,
            _ => null,
        };
    }

    private sealed class ThrottleHandler : DelegatingHandler
    {
        private readonly SemaphoreSlim _concurrency;
        private readonly int? _perMinute;
        private readonly TimeProvider _time;
        private readonly Queue<DateTimeOffset> _recent = new();
        private readonly SemaphoreSlim _window = new(1, 1);

        public ThrottleHandler(SourceHttpOptions options, TimeProvider time)
        {
            _concurrency = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
            _perMinute = options.RequestsPerMinute is > 0 ? options.RequestsPerMinute : null;
            _time = time;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WaitForSlotAsync(cancellationToken).ConfigureAwait(false);
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _concurrency.Release();
            }
        }

        private async Task WaitForSlotAsync(CancellationToken cancellationToken)
        {
            if (_perMinute is not { } limit) return;
            await _window.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _time.GetUtcNow();
                while (_recent.Count > 0 && now - _recent.Peek() >= TimeSpan.FromMinutes(1)) _recent.Dequeue();
                if (_recent.Count >= limit)
                {
                    var wait = _recent.Peek() + TimeSpan.FromMinutes(1) - now;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
                    _recent.Dequeue();
                }
                _recent.Enqueue(_time.GetUtcNow());
            }
            finally
            {
                _window.Release();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _concurrency.Dispose(); _window.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class AuthHandler(ISourceCredentials credentials, SourceAuthMethod? method, Uri? accountAddress) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // The sign-in belongs to the account's service. A declared host that is not the account's (a file download link on a
            // storage host, say) gets the request without it, so the token is never sent to a third party.
            if (accountAddress is not null && request.RequestUri is { } target && !string.Equals(target.Host, accountAddress.Host, StringComparison.OrdinalIgnoreCase))
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var credential = await credentials.GetAsync(cancellationToken).ConfigureAwait(false);
            Attach(request, credential);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

            SourceCredential renewed;
            try
            {
                renewed = await credentials.RefreshAsync(credential, cancellationToken).ConfigureAwait(false);
            }
            catch (SourceException)
            {
                // Cannot renew: the adapter sees the 401 and reports Authentication; the host asks the user to sign in.
                return response;
            }
            response.Dispose();
            var retry = await CloneAsync(request).ConfigureAwait(false);
            Attach(retry, renewed);
            return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        }

        private void Attach(HttpRequestMessage request, SourceCredential credential)
        {
            switch (method)
            {
                case ApiTokenAuth token when !string.Equals(token.HeaderName, "Authorization", StringComparison.OrdinalIgnoreCase):
                    request.Headers.Remove(token.HeaderName);
                    request.Headers.TryAddWithoutValidation(token.HeaderName, token.Scheme is { Length: > 0 } s ? $"{s} {credential.Value}" : credential.Value);
                    break;
                case ApiTokenAuth { Scheme: null or "" }:
                    request.Headers.TryAddWithoutValidation("Authorization", credential.Value);
                    break;
                default:
                    request.Headers.Authorization = new AuthenticationHeaderValue(string.IsNullOrEmpty(credential.Scheme) ? "Bearer" : credential.Scheme, credential.Value);
                    break;
            }
        }
    }

    private sealed class LoggingHandler(IPluginDiagnostics diagnostics, bool enabled, TimeProvider time) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!enabled) return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var started = time.GetTimestamp();
            var what = $"{request.Method} {request.RequestUri?.Host}{request.RequestUri?.AbsolutePath}";
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                diagnostics.Write(response.IsSuccessStatusCode ? DiagnosticLevel.Info : DiagnosticLevel.Warning,
                    $"{what} → {(int)response.StatusCode} in {time.GetElapsedTime(started).TotalMilliseconds:F0} ms");
                return response;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                diagnostics.Write(DiagnosticLevel.Warning, $"{what} failed after {time.GetElapsedTime(started).TotalMilliseconds:F0} ms: {ex.GetType().Name}");
                throw;
            }
        }
    }
}

/// <summary>Turns HTTP failures into <see cref="SourceError"/>s with the kind the host acts on.</summary>
public static class SourceHttpErrors
{
    public static SourceError FromResponse(HttpResponseMessage response, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        var status = (int)response.StatusCode;
        var kind = status switch
        {
            401 => SourceErrorKind.Authentication,
            403 => SourceErrorKind.Authorization,
            404 or 410 => SourceErrorKind.NotFound,
            409 or 412 => SourceErrorKind.Conflict,
            400 or 422 => SourceErrorKind.Validation,
            429 => SourceErrorKind.RateLimited,
            408 or 504 => SourceErrorKind.Timeout,
            501 => SourceErrorKind.Unsupported,
            >= 500 => SourceErrorKind.Unavailable,
            _ => SourceErrorKind.Unknown,
        };
        var text = new StringBuilder(message ?? $"The service answered {status} {response.ReasonPhrase}.");
        return new SourceError(kind, text.ToString())
        {
            RetryAfter = response.Headers.RetryAfter?.Delta,
            RemoteCode = status.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
