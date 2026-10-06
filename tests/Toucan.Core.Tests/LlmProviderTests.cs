using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toucan.Core.Models;
using Toucan.Core.Services.Providers;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>Claude and Gemini against a stub HTTP handler: request shape, reply parsing and error reporting. No network, no real keys.</summary>
public class LlmProviderTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static PretranslationOptions Options(params (string Key, string Value)[] values) =>
        new() { ProviderOptions = values.ToDictionary(v => v.Key, v => v.Value) };

    private static PretranslationJob[] Jobs() => [new("a.title", "Save {{name}}", "en", "id"), new("b.title", "Cancel", "en", "id")];

    private const string ClaudeReply = """{"content":[{"type":"text","text":"[\"Simpan {{name}}\",\"Batal\"]"}]}""";
    private const string GeminiReply = """{"candidates":[{"content":{"parts":[{"text":"```json\n[\"Simpan {{name}}\",\"Batal\"]\n```"}]}}]}""";

    [Fact]
    public async Task Claude_SendsMessagesRequestWithKeyHeaders_AndParsesTheReply()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ClaudeReply);
        var provider = new ClaudeTranslationProvider(new HttpClient(handler));

        var results = (await provider.PretranslateAsync(Jobs(), Options(("api_key", "sk-test"), ("context", "Banking app"), ("formality", "formal")))).ToList();

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("sk-test", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());
        Assert.Null(handler.Request.Headers.Authorization);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("claude-haiku-4-5-20251001", body.RootElement.GetProperty("model").GetString());
        var system = body.RootElement.GetProperty("system").GetString()!;
        Assert.Contains("from en to id", system, StringComparison.Ordinal);
        Assert.Contains("Banking app", system, StringComparison.Ordinal);
        Assert.Contains("formal", system, StringComparison.Ordinal);
        Assert.Equal("""["Save {{name}}","Cancel"]""", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(["Simpan {{name}}", "Batal"], results.Select(r => r.TranslatedValue));
        Assert.Equal(["a.title", "b.title"], results.Select(r => r.Namespace));
    }

    [Fact]
    public async Task Gemini_PutsTheKeyInAHeaderNotTheUrl_AndStripsCodeFences()
    {
        var handler = new StubHandler(HttpStatusCode.OK, GeminiReply);
        var provider = new GeminiTranslationProvider(new HttpClient(handler));

        var results = (await provider.PretranslateAsync(Jobs(), Options(("api_key", "AIza-test"), ("model", "gemini-test")))).ToList();

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent", handler.Request!.RequestUri!.ToString());
        Assert.DoesNotContain("AIza-test", handler.Request.RequestUri.ToString(), StringComparison.Ordinal);
        Assert.Equal("AIza-test", handler.Request.Headers.GetValues("x-goog-api-key").Single());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Contains("from en to id", body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString(), StringComparison.Ordinal);

        Assert.Equal(["Simpan {{name}}", "Batal"], results.Select(r => r.TranslatedValue));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("gemini")]
    public async Task MissingKey_FailsEveryItemWithoutCallingTheNetwork(string which)
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new HttpClient(handler);
        LlmTranslationProvider provider = which == "claude" ? new ClaudeTranslationProvider(client) : new GeminiTranslationProvider(client);
        var envVar = which == "claude" ? "ANTHROPIC_API_KEY" : "GEMINI_API_KEY";
        var saved = Environment.GetEnvironmentVariable(envVar);
        Environment.SetEnvironmentVariable(envVar, null);
        try
        {
            var results = (await provider.PretranslateAsync(Jobs(), Options())).ToList();

            Assert.Null(handler.Request);
            Assert.All(results, r => { Assert.False(r.Succeeded); Assert.Equal("No API key configured", r.ErrorMessage); });
        }
        finally { Environment.SetEnvironmentVariable(envVar, saved); }
    }

    [Fact]
    public async Task HttpError_ReportsTheStatusAndTheProvidersMessage()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""");
        var provider = new ClaudeTranslationProvider(new HttpClient(handler));

        var results = (await provider.PretranslateAsync(Jobs(), Options(("api_key", "bad")))).ToList();

        Assert.All(results, r => Assert.Equal("HTTP 401 Unauthorized: invalid x-api-key", r.ErrorMessage));
    }

    [Fact]
    public async Task ReplyWithWrongCount_MarksTheMissingItemsAsFailed()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"content":[{"type":"text","text":"[\"Simpan\"]"}]}""");
        var provider = new ClaudeTranslationProvider(new HttpClient(handler));

        var results = (await provider.PretranslateAsync(Jobs(), Options(("api_key", "k")))).ToList();

        Assert.True(results[0].Succeeded);
        Assert.False(results[1].Succeeded);
        Assert.Equal("No translation returned", results[1].ErrorMessage);
    }

    [Fact]
    public async Task CustomPromptReplacesTheDefaultButKeepsTheContext()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ClaudeReply);
        var provider = new ClaudeTranslationProvider(new HttpClient(handler));

        await provider.PretranslateAsync(Jobs(), Options(("api_key", "k"), ("prompt", "Translate casually."), ("context", "Game")));

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var system = body.RootElement.GetProperty("system").GetString()!;
        Assert.StartsWith("Translate casually.", system, StringComparison.Ordinal);
        Assert.Contains("Game", system, StringComparison.Ordinal);
        Assert.DoesNotContain("professional translator", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoreThanTwentyTexts_AreSentInSeveralRequests()
    {
        var calls = 0;
        var handler = new CountingHandler(() => { calls++; return ClaudeReplyFor(calls == 1 ? 20 : 5); });
        var provider = new ClaudeTranslationProvider(new HttpClient(handler));
        var jobs = Enumerable.Range(0, 25).Select(i => new PretranslationJob($"k{i}", $"Text {i}", "en", "id")).ToArray();

        var results = (await provider.PretranslateAsync(jobs, Options(("api_key", "k")))).ToList();

        Assert.Equal(2, calls);
        Assert.Equal(25, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded));
    }

    private static string ClaudeReplyFor(int count) =>
        JsonSerializer.Serialize(new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(Enumerable.Range(0, count).Select(i => $"t{i}")) } } });

    private sealed class CountingHandler(Func<string> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(next(), Encoding.UTF8, "application/json") });
    }

    [Fact]
    public void BothProvidersAreListedWithTheirDefaults()
    {
        var claude = new ClaudeTranslationProvider().Definition!;
        var gemini = new GeminiTranslationProvider().Definition!;

        Assert.Equal("Claude", claude.Name);
        Assert.Equal("claude-haiku-4-5-20251001", claude.DefaultValues["model"]);
        Assert.Contains("api_key", claude.SecretFields.Keys);
        Assert.Equal("Gemini", gemini.Name);
        Assert.Equal("gemini-flash-latest", gemini.DefaultValues["model"]);
        Assert.Contains("api_key", gemini.SecretFields.Keys);
    }
}
