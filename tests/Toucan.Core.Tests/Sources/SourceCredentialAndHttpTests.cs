using System.Net;
using System.Text;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

public sealed class SourceCredentialAndHttpTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-credentials-").FullName;
    private readonly MemorySecretService _secrets = new();
    private readonly DiagnosticsService _diagnostics = new();
    private readonly SourceAccount _account = new() { Id = "acc_1", Adapter = "acme.x", BaseUrl = new Uri("https://api.example.com/"), AuthMethod = "oauth", DisplayName = "Work" };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => TempFolder.TryDelete(_root);

    private SourceCredentialBroker Broker() => new(_secrets, _diagnostics, Path.Combine(_root, "locks"));

    private sealed class CountingRefresher(Func<StoredSourceCredential, StoredSourceCredential>? renew = null, Exception? fail = null) : ISourceTokenRefresher
    {
        public int Calls;
        public async Task<StoredSourceCredential> RefreshAsync(SourceAccount account, StoredSourceCredential current, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(20, cancellationToken);
            if (fail is not null) throw fail;
            return renew?.Invoke(current) ?? current with { Value = current.Value + "-renewed", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        }
    }

    [Fact]
    public async Task StoredCredentialsAreRedactedAndMissingOnesAskForSignIn()
    {
        var broker = Broker();
        var credentials = broker.For(_account);
        var missing = await Assert.ThrowsAsync<SourceException>(() => credentials.GetAsync(Ct));
        Assert.Equal(SourceErrorKind.Authentication, missing.Error.Kind);

        broker.Store(_account, new StoredSourceCredential("oauth", "Bearer", "access-secret-123456", "refresh-secret-123456"));
        Assert.Equal("access-secret-123456", (await credentials.GetAsync(Ct)).Value);
        var redacted = _diagnostics.Redact("token access-secret-123456 and refresh-secret-123456");
        Assert.DoesNotContain("secret-123456", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", broker.Read(_account)!.ToString(), StringComparison.Ordinal);
        Assert.True(broker.Forget(_account));
        Assert.Null(broker.Read(_account));
    }

    [Fact]
    public async Task ConcurrentRefreshesOfOneRejectedCredentialRefreshOnce()
    {
        var broker = Broker();
        broker.Store(_account, new StoredSourceCredential("oauth", "Bearer", "old", "refresh"));
        var refresher = new CountingRefresher();
        var credentials = broker.For(_account, refresher: refresher);
        var rejected = await credentials.GetAsync(Ct);

        var renewed = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => credentials.RefreshAsync(rejected, Ct)));

        Assert.Equal(1, refresher.Calls);
        Assert.All(renewed, c => Assert.Equal("old-renewed", c.Value));
        Assert.Equal("old-renewed", broker.Read(_account)!.Value);
        Assert.Equal("refresh", broker.Read(_account)!.RefreshToken);
    }

    [Fact]
    public async Task ACredentialCloseToExpiryIsRenewedBeforeUse()
    {
        var broker = Broker();
        broker.Store(_account, new StoredSourceCredential("oauth", "Bearer", "old", "refresh", DateTimeOffset.UtcNow.AddSeconds(10)));
        var refresher = new CountingRefresher();

        Assert.Equal("old-renewed", (await broker.For(_account, refresher: refresher).GetAsync(Ct)).Value);
        Assert.Equal(1, refresher.Calls);
    }

    [Fact]
    public async Task WithoutARefreshTokenOrWhenRenewalFailsTheUserSignsInAgain()
    {
        var broker = Broker();
        broker.Store(_account, new StoredSourceCredential("token", "Bearer", "pasted"));
        var error = await Assert.ThrowsAsync<SourceException>(() => broker.For(_account, refresher: new CountingRefresher()).RefreshAsync(new("Bearer", "pasted"), Ct));
        Assert.Equal(SourceErrorKind.Authentication, error.Error.Kind);
        Assert.Contains("Sign in to Work again", error.Message, StringComparison.Ordinal);

        broker.Store(_account, new StoredSourceCredential("oauth", "Bearer", "old", "refresh"));
        var failing = new CountingRefresher(fail: new SourceException(new SourceError(SourceErrorKind.Authentication, "invalid_grant")));
        error = await Assert.ThrowsAsync<SourceException>(() => broker.For(_account, refresher: failing).RefreshAsync(new("Bearer", "old"), Ct));
        Assert.Contains("could not be renewed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEnvironmentTokenWinsAndIsNeverStored()
    {
        var variable = SourceCredentialBroker.EnvironmentVariableFor("con-1a.b");
        Assert.Equal("TOUCAN_SOURCE_TOKEN_CON_1A_B", variable);
        Environment.SetEnvironmentVariable(variable, "ci-token-abcdef");
        try
        {
            var credential = await Broker().For(_account, "con-1a.b").GetAsync(Ct);
            Assert.Equal("ci-token-abcdef", credential.Value);
            Assert.Equal(0, _secrets.Writes);
            Assert.DoesNotContain("ci-token-abcdef", _diagnostics.Redact("x ci-token-abcdef"), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    // ───────────────────────────── HTTP pipeline ─────────────────────────────

    private SourceHttpClientFactory Factory(StubHandler server, ISourceCredentials? credentials = null, SourceAuthMethod? method = null,
        SourceHttpOptions? options = null, string[]? hosts = null, bool log = false) =>
        new(new SourceNetworkPolicy(new NetworkAccess { Hosts = hosts ?? ["api.example.com", "*.cdn.example.com"] }, new Uri("https://self.hosted.test/")),
            credentials, method, new PluginDiagnostics("acme.x", _diagnostics),
            (options ?? new SourceHttpOptions { BaseRetryDelay = TimeSpan.FromMilliseconds(1) }) with { PrimaryHandler = () => server, LogRequests = log, UserAgent = "Toucan/test acme.x" });

    [Theory]
    [InlineData("https://evil.example.com/x", "not among the hosts")]
    [InlineData("http://api.example.com/x", "use HTTPS")]
    public async Task RequestsOutsideTheDeclaredHostsAreBlocked(string address, string expected)
    {
        var server = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Factory(server).Create();

        var error = await Assert.ThrowsAsync<SourceException>(() => client.GetAsync(new Uri(address), Ct));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Empty(server.Requests);
        Assert.Contains(_diagnostics.Recent("acme.x"), l => l.Message.Contains("blocked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeclaredHostsTheAccountAddressAndLoopbackAreAllowed()
    {
        var server = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Factory(server).Create();
        foreach (var url in new[] { "https://api.example.com/a", "https://eu.cdn.example.com/b", "https://self.hosted.test/c", "http://127.0.0.1:9/d" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri(url), Ct)).StatusCode);
        Assert.All(server.Requests, r => Assert.Equal("Toucan/test,acme.x", r.Headers["User-Agent"])); // the stub joins header values with commas
    }

    [Fact]
    public async Task RedirectsAreFollowedOnlyToAllowedHosts()
    {
        var server = new StubHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/start" => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://eu.cdn.example.com/file") } },
            "/leak" => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://collector.evil.example/") } },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("done") },
        });
        using var client = Factory(server).Create();

        Assert.Equal("done", await (await client.GetAsync(new Uri("https://api.example.com/start"), Ct)).Content.ReadAsStringAsync(Ct));
        await Assert.ThrowsAsync<SourceException>(() => client.GetAsync(new Uri("https://api.example.com/leak"), Ct));
        Assert.DoesNotContain(server.Requests, r => r.Url.Host == "collector.evil.example");
    }

    [Fact]
    public async Task A401RefreshesTheCredentialOnceAndRetries()
    {
        var broker = Broker();
        broker.Store(_account, new StoredSourceCredential("oauth", "Bearer", "expired", "refresh"));
        var refresher = new CountingRefresher(c => c with { Value = "fresh" });
        var server = new StubHandler((request, _) => request.Headers.Authorization?.Parameter == "fresh"
            ? new HttpResponseMessage(HttpStatusCode.OK) : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = Factory(server, broker.For(_account, refresher: refresher), new OAuthDeviceCodeAuth("oauth", "OAuth", new Uri("https://api.example.com/d"), new Uri("https://api.example.com/t"), "id", [])).Create();

        using var content = new StringContent("{\"a\":1}");
        var response = await client.PostAsync(new Uri("https://api.example.com/keys"), content, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Bearer expired", "Bearer fresh"], server.Requests.Select(r => r.Authorization));
        Assert.Equal("{\"a\":1}", server.Requests[1].Body);
        Assert.Equal(1, refresher.Calls);
    }

    [Fact]
    public async Task TokensGoInTheHeaderTheMethodNames()
    {
        var broker = Broker();
        broker.Store(_account, new StoredSourceCredential("token", string.Empty, "raw-token-1234567"));
        var server = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Factory(server, broker.For(_account), new ApiTokenAuth("token", "Token", "X-Api-Token", null)).Create();

        await client.GetAsync(new Uri("https://api.example.com/a"), Ct);

        Assert.Equal("raw-token-1234567", server.Requests.Single().Headers["X-Api-Token"]);
        Assert.Null(server.Requests.Single().Authorization);
    }

    [Fact]
    public async Task IdempotentRequestsAreRetriedHonouringRetryAfter()
    {
        var server = new StubHandler((_, n) => n < 2
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(5)) } }
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Factory(server).Create();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("https://api.example.com/a"), Ct)).StatusCode);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task PostsAreRetriedOnlyWithAnIdempotencyKey()
    {
        var server = new StubHandler((_, n) => n < 2 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = Factory(server).Create();

        using var plain = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/keys") { Content = new StringContent("x") };
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(plain, Ct)).StatusCode);
        Assert.Single(server.Requests);

        using var keyed = new HttpRequestMessage(HttpMethod.Post, "https://api.example.com/keys") { Content = new StringContent("y") };
        keyed.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(keyed, Ct)).StatusCode);
        Assert.Equal(["x", "y", "y"], server.Requests.Select(r => r.Body));
    }

    [Fact]
    public async Task ALongRetryAfterIsReturnedInsteadOfWaited()
    {
        var server = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10)) } });
        using var client = Factory(server).Create();

        var response = await client.GetAsync(new Uri("https://api.example.com/a"), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(server.Requests);
        var error = SourceHttpErrors.FromResponse(response);
        Assert.Equal(SourceErrorKind.RateLimited, error.Kind);
        Assert.Equal(TimeSpan.FromMinutes(10), error.RetryAfter);
    }

    [Fact]
    public async Task LoggingRecordsMethodHostPathAndStatusOnly()
    {
        var server = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = Factory(server, log: true).Create();

        await client.GetAsync(new Uri("https://api.example.com/keys/42?access_token=leaky-secret-999"), Ct);

        var line = Assert.Single(_diagnostics.Recent("acme.x")).Message;
        Assert.Contains("GET api.example.com/keys/42 → 404", line, StringComparison.Ordinal);
        Assert.DoesNotContain("leaky", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrencyIsLimitedPerAccount()
    {
        var running = 0;
        var peak = 0;
        var server = new StubHandler((_, _) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            Thread.Sleep(20);
            Interlocked.Decrement(ref running);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var factory = Factory(server, options: new SourceHttpOptions { MaxConcurrency = 2 });

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () => { using var c = factory.Create(); await c.GetAsync(new Uri("https://api.example.com/a"), Ct); }, Ct)));

        Assert.True(peak <= 2, $"peak {peak}");
        Assert.Equal(8, server.Requests.Count);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }

    [Fact]
    public void HttpStatusesMapToErrorKinds()
    {
        Assert.Equal(SourceErrorKind.Authentication, SourceHttpErrors.FromResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized)).Kind);
        Assert.Equal(SourceErrorKind.Authorization, SourceHttpErrors.FromResponse(new HttpResponseMessage(HttpStatusCode.Forbidden)).Kind);
        Assert.Equal(SourceErrorKind.Conflict, SourceHttpErrors.FromResponse(new HttpResponseMessage(HttpStatusCode.PreconditionFailed)).Kind);
        Assert.Equal(SourceErrorKind.Unavailable, SourceHttpErrors.FromResponse(new HttpResponseMessage(HttpStatusCode.BadGateway)).Kind);
        Assert.Equal(SourceErrorKind.Validation, SourceHttpErrors.FromResponse(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)).Kind);
    }

    // ───────────────────────────── OAuth ─────────────────────────────

    private static readonly OAuthDeviceCodeAuth s_device = new("device", "Device", new Uri("https://api.example.com/oauth/device"),
        new Uri("https://api.example.com/oauth/token"), "client-1", ["read", "write"], new Uri("https://api.example.com/oauth/revoke"));

    private static string DeviceAnswer(string verification = "https://api.example.com/activate") =>
        $$"""{"device_code":"dev-1","user_code":"ABCD-EFGH","verification_uri":"{{verification}}","verification_uri_complete":"{{verification}}?code=ABCD","expires_in":600,"interval":0}""";

    [Fact]
    public async Task DeviceCodeSignInPollsThroughPendingAndSlowDown()
    {
        var server = new StubHandler((request, n) => request.RequestUri!.AbsolutePath switch
        {
            "/oauth/device" => StubHandler.Json(HttpStatusCode.OK, DeviceAnswer()),
            _ when n == 1 => StubHandler.Json(HttpStatusCode.BadRequest, """{"error":"authorization_pending"}"""),
            _ when n == 2 => StubHandler.Json(HttpStatusCode.BadRequest, """{"error":"authorization_pending"}"""),
            _ => StubHandler.Json(HttpStatusCode.OK, """{"access_token":"at-xyz","token_type":"bearer","refresh_token":"rt-xyz","expires_in":3600}"""),
        });
        using var client = Factory(server).Create();
        DeviceCodePrompt? shown = null;

        var credential = await SourceOAuth.DeviceCodeAsync(s_device, client, new SourceNetworkPolicy(new NetworkAccess { Hosts = ["api.example.com"] }, null),
            p => { shown = p; return Task.CompletedTask; }, TimeProvider.System, Ct);

        Assert.Equal("ABCD-EFGH", shown!.UserCode);
        Assert.Equal("at-xyz", credential.Value);
        Assert.Equal("Bearer", credential.Scheme);
        Assert.Equal("rt-xyz", credential.RefreshToken);
        Assert.NotNull(credential.ExpiresAt);
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", server.Requests[^1].Body, StringComparison.Ordinal);
        Assert.Contains("scope=read+write", server.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceCodeSignInRefusesAVerificationPageOnAnotherHost()
    {
        var server = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, DeviceAnswer("https://phish.example.net/activate")));
        using var client = Factory(server).Create();
        var prompted = false;

        var error = await Assert.ThrowsAsync<SourceException>(() => SourceOAuth.DeviceCodeAsync(s_device, client,
            new SourceNetworkPolicy(new NetworkAccess { Hosts = ["api.example.com"] }, null), _ => { prompted = true; return Task.CompletedTask; }, TimeProvider.System, Ct));

        Assert.Contains("phish.example.net", error.Message, StringComparison.Ordinal);
        Assert.False(prompted);
    }

    [Fact]
    public async Task DeviceCodeSignInStopsWhenDeclined()
    {
        var server = new StubHandler((request, _) => request.RequestUri!.AbsolutePath == "/oauth/device"
            ? StubHandler.Json(HttpStatusCode.OK, DeviceAnswer()) : StubHandler.Json(HttpStatusCode.BadRequest, """{"error":"access_denied"}"""));
        using var client = Factory(server).Create();

        var error = await Assert.ThrowsAsync<SourceException>(() => SourceOAuth.DeviceCodeAsync(s_device, client,
            new SourceNetworkPolicy(new NetworkAccess { Hosts = ["api.example.com"] }, null), _ => Task.CompletedTask, TimeProvider.System, Ct));
        Assert.Equal(SourceErrorKind.Authorization, error.Error.Kind);
    }

    /// <summary>Plays the provider's login page: ignores a forged answer first, then redirects back with the code.</summary>
    private sealed class FakeBrowser(string code) : IBrowserLauncher
    {
        public Uri? Opened { get; private set; }

        public async Task OpenAsync(Uri url, CancellationToken cancellationToken)
        {
            Opened = url;
            var query = SourceOAuth_ParseQuery(url.Query);
            var redirect = query["redirect_uri"];
            _ = Task.Run(async () =>
            {
                using var http = new HttpClient();
                var forged = await http.GetAsync(new Uri($"{redirect}?code=stolen&state=wrong"), cancellationToken);
                Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
                await http.GetAsync(new Uri($"{redirect}?code={code}&state={Uri.EscapeDataString(query["state"])}"), cancellationToken);
            }, cancellationToken);
            await Task.CompletedTask;
        }

        private static Dictionary<string, string> SourceOAuth_ParseQuery(string query) =>
            query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
    }

    [Fact]
    public async Task BrowserSignInUsesPkceAndTheLoopbackRedirect()
    {
        var method = new OAuthAuthorizationCodeAuth("oauth", "Browser", new Uri("https://api.example.com/authorize?tenant=t1"), new Uri("https://api.example.com/token"), "client-1", ["read"]);
        var server = new StubHandler((_, _) => StubHandler.Json(HttpStatusCode.OK, """{"access_token":"at-browser","refresh_token":"rt-browser"}"""));
        using var client = Factory(server).Create();
        var browser = new FakeBrowser("code-123");

        var credential = await SourceOAuth.AuthorizationCodeAsync(method, client, browser, TimeProvider.System, TimeSpan.FromSeconds(20), Ct);

        Assert.Equal("at-browser", credential.Value);
        var opened = browser.Opened!.Query;
        Assert.Contains("tenant=t1", opened, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", opened, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A", opened, StringComparison.Ordinal);
        var form = server.Requests.Single().Body;
        Assert.Contains("code=code-123", form, StringComparison.Ordinal);
        var verifier = Uri.UnescapeDataString(form.Split('&').Single(p => p.StartsWith("code_verifier=", StringComparison.Ordinal))["code_verifier=".Length..]);
        var challenge = Uri.UnescapeDataString(opened.TrimStart('?').Split('&').Single(p => p.StartsWith("code_challenge=", StringComparison.Ordinal))["code_challenge=".Length..]);
        Assert.Equal(challenge, SourceOAuth.ToWebSafeBase64(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    [Fact]
    public async Task BrowserSignInTimesOutWhenNeverFinished()
    {
        var method = new OAuthAuthorizationCodeAuth("oauth", "Browser", new Uri("https://api.example.com/authorize"), new Uri("https://api.example.com/token"), "c", []);
        using var client = Factory(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK))).Create();
        var idle = new IdleBrowser();

        var error = await Assert.ThrowsAsync<SourceException>(() => SourceOAuth.AuthorizationCodeAsync(method, client, idle, TimeProvider.System, TimeSpan.FromMilliseconds(300), Ct));
        Assert.Equal(SourceErrorKind.Timeout, error.Error.Kind);
    }

    private sealed class IdleBrowser : IBrowserLauncher
    {
        public Task OpenAsync(Uri url, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
