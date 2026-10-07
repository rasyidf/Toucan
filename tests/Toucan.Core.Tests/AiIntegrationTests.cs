using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Core.Services.Ai;
using Toucan.Core.Services.Providers;
using Toucan.Core.Services.Providers.Ai;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>
/// AI Integration end to end against a stub HTTP handler and temporary folders: the app-wide switch, the services'
/// request shapes, prompts and their overrides, the AI translation provider, Analyze and Clarity. No network, no real keys.
/// </summary>
[Collection(nameof(EnvironmentVariableTests))]
public sealed class AiIntegrationTests : IDisposable
{
    private sealed class StubHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        public StubHandler(HttpStatusCode status, string body) : this((_, _) => (status, body)) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            var (status, reply) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }

    private readonly string _root = Directory.CreateTempSubdirectory("toucan-ai-").FullName;
    private readonly Dictionary<string, string?> _savedEnv = [];

    public AiIntegrationTests()
    {
        // A developer's own keys or CI switch must not leak into these tests.
        foreach (var name in new[] { "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "GEMINI_API_KEY", AiService.BackendEnvironmentVariable })
        {
            _savedEnv[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _savedEnv) Environment.SetEnvironmentVariable(name, value);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed record Setup(AiService Ai, AiSettingsStore Store, SecretService Secrets, PromptLibrary Prompts, StubHandler Handler);

    private Setup Create(StubHandler handler, Action<AiSettings>? configure = null, string? apiKey = "sk-test")
    {
        var secrets = new SecretService(new SecureStorageService(Path.Combine(_root, "secret.key")), Path.Combine(_root, "secrets.json"));
        var store = new AiSettingsStore(Path.Combine(_root, "ai.json"));
        var prompts = new PromptLibrary(BuiltInAiFeatures.All, Path.Combine(_root, "prompts"));
        var http = new HttpClient(handler);
        var ai = new AiService([new AnthropicAiBackend(http), new OpenAiCompatibleBackend(http), new GeminiAiBackend(http)], store, secrets, prompts);

        var settings = store.Load();
        settings.Enabled = true;
        configure?.Invoke(settings);
        store.Save(settings);
        if (apiKey != null) secrets.SetSecret(SecretKeys.Ai(settings.Backend), apiKey);
        return new Setup(ai, store, secrets, prompts, handler);
    }

    private static string ClaudeReply(string text) => JsonSerializer.Serialize(new { content = new[] { new { type = "text", text } } });

    private static string ClaudeArray(params string[] items) => ClaudeReply(JsonSerializer.Serialize(items));

    private static AiRequest Translate(string input = """["Save"]""", string? projectPath = null) => new()
    {
        FeatureId = AiFeatureIds.Translate,
        Input = input,
        ProjectPath = projectPath,
        Variables = new Dictionary<string, string?> { ["source_language"] = "en", ["target_language"] = "id", ["context"] = "Banking app", ["formality"] = "formal" },
    };

    // ───────────────────────── The switch and setup ─────────────────────────

    [Fact]
    public async Task AiIsOffByDefault_AndNothingIsSent()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeArray("Simpan")), s => s.Enabled = false);

        Assert.False(setup.Ai.IsEnabled);
        Assert.False(new AiSettings().Enabled);
        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => setup.Ai.CompleteAsync(Translate()));
        Assert.Contains("turned off", ex.Message, StringComparison.Ordinal);
        Assert.Empty(setup.Handler.Calls);
    }

    [Fact]
    public async Task MissingKey_IsReportedWithoutCallingTheService()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, "{}"), apiKey: null);

        Assert.False(setup.Ai.GetStatus().IsReady);
        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => setup.Ai.CompleteAsync(Translate()));
        Assert.Contains("no API key", ex.Message, StringComparison.Ordinal);
        Assert.Empty(setup.Handler.Calls);
    }

    [Fact]
    public async Task ATurnedOffFeature_IsNotRun()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, "[]"), s => s.Features[AiFeatureIds.Analyze] = new AiFeatureSettings { Enabled = false });

        Assert.True(setup.Ai.IsFeatureEnabled(AiFeatureIds.Translate));
        Assert.False(setup.Ai.IsFeatureEnabled(AiFeatureIds.Analyze));
        await Assert.ThrowsAsync<AiUnavailableException>(() => setup.Ai.CompleteAsync(new AiRequest { FeatureId = AiFeatureIds.Analyze, Input = "x" }));
    }

    [Fact]
    public async Task TheCiVariable_TurnsAiOnForTheProcessOnly()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeArray("Simpan")), s => s.Enabled = false);
        Environment.SetEnvironmentVariable(AiService.BackendEnvironmentVariable, "anthropic");

        Assert.Equal("Simpan", JsonSerializer.Deserialize<string[]>(await setup.Ai.CompleteAsync(Translate()))![0]);
        Assert.False(setup.Store.Load().Enabled);
    }

    // ───────────────────────── Services: request shapes ─────────────────────────

    [Fact]
    public async Task Claude_SendsTheRenderedTranslatePromptWithKeyHeaders()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeArray("Simpan")));

        await setup.Ai.CompleteAsync(Translate());

        var (request, body) = Assert.Single(setup.Handler.Calls);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-test", request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
        Assert.Null(request.Headers.Authorization);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("claude-haiku-4-5-20251001", json.RootElement.GetProperty("model").GetString());
        var system = json.RootElement.GetProperty("system").GetString()!;
        Assert.Contains("from en to id", system, StringComparison.Ordinal);
        Assert.Contains("Banking app", system, StringComparison.Ordinal);
        Assert.Contains("Use a formal register", system, StringComparison.Ordinal);
        // Placeholders written in the prompt as examples are not template variables.
        Assert.Contains("{{name}}", system, StringComparison.Ordinal);
        Assert.DoesNotContain("{{#", system, StringComparison.Ordinal);
        Assert.Equal("""["Save"]""", json.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Gemini_PutsTheKeyInAHeaderNotTheUrl()
    {
        const string reply = """{"candidates":[{"content":{"parts":[{"text":"```json\n[\"Simpan\"]\n```"}]}}]}""";
        var setup = Create(new StubHandler(HttpStatusCode.OK, reply), s =>
        {
            s.Backend = GeminiAiBackend.Id;
            s.Backends[GeminiAiBackend.Id] = new AiBackendSettings { Model = "gemini-test" };
        }, apiKey: "AIza-test");

        var text = await setup.Ai.CompleteAsync(Translate());

        var (request, body) = Assert.Single(setup.Handler.Calls);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent", request.RequestUri!.ToString());
        Assert.DoesNotContain("AIza-test", request.RequestUri.ToString(), StringComparison.Ordinal);
        Assert.Equal("AIza-test", request.Headers.GetValues("x-goog-api-key").Single());
        Assert.Contains("application/json", body, StringComparison.Ordinal);
        Assert.Equal(["Simpan"], AiReply.ParseStringArray(text, 1));
    }

    [Fact]
    public async Task OpenAiCompatible_WorksWithoutAKeyForALocalServer()
    {
        const string reply = """{"choices":[{"message":{"content":"[\"Simpan\"]"}}]}""";
        var setup = Create(new StubHandler(HttpStatusCode.OK, reply), s =>
        {
            s.Backend = OpenAiCompatibleBackend.Id;
            s.Backends[OpenAiCompatibleBackend.Id] = new AiBackendSettings { Endpoint = "http://localhost:11434/v1/", Model = "llama3" };
        }, apiKey: null);

        Assert.True(setup.Ai.GetStatus().IsReady);
        await setup.Ai.CompleteAsync(Translate());

        var (request, body) = Assert.Single(setup.Handler.Calls);
        Assert.Equal("http://localhost:11434/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("llama3", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("system", json.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task AFeatureModel_OverridesTheServiceModel()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeReply("[]")), s =>
        {
            s.Backends["anthropic"] = new AiBackendSettings { Model = "claude-small" };
            s.Features[AiFeatureIds.Analyze] = new AiFeatureSettings { Model = "claude-large" };
        });

        await setup.Ai.CompleteAsync(Translate());
        await setup.Ai.CompleteAsync(new AiRequest { FeatureId = AiFeatureIds.Analyze, Input = "x" });

        Assert.Equal(["claude-small", "claude-large"], setup.Handler.Calls.Select(c => JsonDocument.Parse(c.Body).RootElement.GetProperty("model").GetString()));
    }

    [Fact]
    public async Task HttpErrors_CarryTheStatusAndTheServicesMessage()
    {
        var setup = Create(new StubHandler(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}"""));

        var ex = await Assert.ThrowsAsync<AiRequestException>(() => setup.Ai.CompleteAsync(Translate()));
        Assert.Equal("HTTP 401 Unauthorized: invalid x-api-key", ex.Message);
    }

    // ───────────────────────── Prompts ─────────────────────────

    [Fact]
    public void BuiltInPromptsShipForEveryFeature()
    {
        Assert.Equal([AiFeatureIds.Translate, AiFeatureIds.Analyze, AiFeatureIds.Clarity], BuiltInAiFeatures.All.Select(f => f.Id));
        Assert.All(BuiltInAiFeatures.All, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.DefaultPrompt));
            // Every variable a prompt uses is one the feature declares, so the editor lists it.
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(f.DefaultPrompt, @"\{\{[#^/]?([a-z_]+)\}\}"))
                Assert.True(m.Groups[1].Value == "name" || f.Variables.Any(v => v.Name == m.Groups[1].Value), $"{f.Id}: {m.Value}");
        });
    }

    [Fact]
    public void Template_FillsKnownVariables_DropsEmptySections_AndKeepsExamples()
    {
        var text = PromptTemplate.Render(
            "To {{target_language}}.{{#context}} Context: {{context}}.{{/context}}{{^glossary}} No glossary.{{/glossary}} Keep {{name}} and {0}.",
            new Dictionary<string, string?> { ["target_language"] = "fr", ["context"] = "", ["glossary"] = null });

        Assert.Equal("To fr. No glossary. Keep {{name}} and {0}.", text);
    }

    [Fact]
    public async Task ProjectPromptBeatsUserPromptBeatsBuiltIn()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeArray("x")));
        var project = Directory.CreateDirectory(Path.Combine(_root, "project")).FullName;
        string System() => JsonDocument.Parse(setup.Handler.Calls[^1].Body).RootElement.GetProperty("system").GetString()!;

        await setup.Ai.CompleteAsync(Translate(projectPath: project));
        Assert.StartsWith("You are a professional translator", System(), StringComparison.Ordinal);

        setup.Prompts.Save(AiFeatureIds.Translate, "USER to {{target_language}}", PromptSource.User);
        await setup.Ai.CompleteAsync(Translate(projectPath: project));
        Assert.Equal("USER to id", System());

        setup.Prompts.Save(AiFeatureIds.Translate, "PROJECT{{#context}} ({{context}}){{/context}}", PromptSource.Project, project);
        await setup.Ai.CompleteAsync(Translate(projectPath: project));
        Assert.Equal("PROJECT (Banking app)", System());
        Assert.True(File.Exists(Path.Combine(project, ".toucan", "prompts", "translate.md")));

        setup.Prompts.Reset(AiFeatureIds.Translate, PromptSource.Project, project);
        Assert.Equal(PromptSource.User, setup.Prompts.GetPrompt(AiFeatureIds.Translate, project).Source);
    }

    [Fact]
    public void SavingTheDefaultAsTheUsersPrompt_RemovesTheOverride()
    {
        var prompts = new PromptLibrary(BuiltInAiFeatures.All, Path.Combine(_root, "prompts"));
        prompts.Save(AiFeatureIds.Clarity, "Mine", PromptSource.User);
        Assert.Equal(PromptSource.User, prompts.GetPrompt(AiFeatureIds.Clarity).Source);

        prompts.Save(AiFeatureIds.Clarity, BuiltInAiFeatures.Read(AiFeatureIds.Clarity) + "\n\n", PromptSource.User);

        Assert.Equal(PromptSource.BuiltIn, prompts.GetPrompt(AiFeatureIds.Clarity).Source);
        Assert.False(File.Exists(Path.Combine(_root, "prompts", "clarity.md")));
    }

    // ───────────────────────── The AI translation provider ─────────────────────────

    private static PretranslationJob[] Jobs() => [new("a.title", "Save ⟨0⟩", "en", "id"), new("b.title", "Cancel", "en", "id")];

    [Fact]
    public async Task AiProvider_WhileAiIsOff_FailsEveryItemWithTheReason()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, "{}"), s => s.Enabled = false);

        var results = (await new AiTranslationProvider(setup.Ai).PretranslateAsync(Jobs())).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => { Assert.False(r.Succeeded); Assert.Contains("AI is turned off", r.ErrorMessage, StringComparison.Ordinal); });
        Assert.Empty(setup.Handler.Calls);
    }

    [Fact]
    public async Task AiProvider_TranslatesInOrder_AndMarksMissingReplies()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeArray("Simpan ⟨0⟩")));

        var results = (await new AiTranslationProvider(setup.Ai).PretranslateAsync(Jobs())).ToList();

        Assert.Equal(["a.title", "b.title"], results.Select(r => r.Namespace));
        Assert.Equal("Simpan ⟨0⟩", results[0].TranslatedValue);
        Assert.Equal("AI", results[0].Provider);
        Assert.False(results[1].Succeeded);
        Assert.Equal("No translation returned", results[1].ErrorMessage);
    }

    [Fact]
    public async Task AiProvider_BatchesPerLanguageAndByTwenty()
    {
        var setup = Create(new StubHandler((_, body) =>
        {
            var count = JsonSerializer.Deserialize<string[]>(JsonDocument.Parse(body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!)!.Length;
            return (HttpStatusCode.OK, ClaudeArray([.. Enumerable.Range(0, count).Select(i => $"t{i}")]));
        }));
        var jobs = Enumerable.Range(0, 25).Select(i => new PretranslationJob($"k{i}", $"Text {i}", "en", "id"))
            .Append(new PretranslationJob("k0", "Text 0", "en", "fr")).ToArray();

        var results = (await new AiTranslationProvider(setup.Ai).PretranslateAsync(jobs)).ToList();

        Assert.Equal(3, setup.Handler.Calls.Count);
        Assert.Equal(26, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Contains("to fr", JsonDocument.Parse(setup.Handler.Calls[2].Body).RootElement.GetProperty("system").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiProvider_WithoutAKey_ReportsItOnceForEveryItem()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, "{}"), apiKey: null);
        var jobs = Enumerable.Range(0, 45).Select(i => new PretranslationJob($"k{i}", "x", "en", i % 2 == 0 ? "id" : "fr")).ToArray();

        var results = (await new AiTranslationProvider(setup.Ai).PretranslateAsync(jobs)).ToList();

        Assert.Equal(45, results.Count);
        Assert.Equal(45, results.Select(r => (r.Namespace, r.Language)).Distinct().Count());
        Assert.All(results, r => Assert.Contains("no API key", r.ErrorMessage, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LegacyProviderNames_RouteToTheAiProvider()
    {
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeArray("Batal")));
        var service = new PretranslationService([new MockTranslationProvider(), new AiTranslationProvider(setup.Ai)]);
        var items = new List<TranslationItem> { new() { Namespace = "k", Language = "en", Value = "Cancel" }, new() { Namespace = "k", Language = "id", Value = "" } };

        var result = await service.PreTranslateAsync(new PretranslationRequest { Provider = "Claude", Items = items, Options = new PretranslationOptions { PreviewOnly = true } });

        Assert.Equal("AI", Assert.Single(result.Items).Provider);
    }

    // ───────────────────────── Analyze and Clarity ─────────────────────────

    [Fact]
    public async Task Analyze_ReadsFindings_AndSendsTheGlossary()
    {
        const string findings = """[{"index":1,"severity":"error","issue":"Hold means freeze funds","suggestion":"Bekukan","confidence":0.9},{"index":7,"issue":"out of range"}]""";
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeReply("Here you go:\n" + findings)));
        var analyzer = new TranslationAnalyzerService(setup.Ai);

        var results = (await analyzer.AnalyzeAsync(new AnalysisRequest
        {
            ApplicationContext = "Banking app",
            Items = [new AnalysisItem("a", "Save", "Simpan", "id"), new AnalysisItem("b", "Hold", "Pegang", "id")],
            Glossary = new() { ["Hold"] = new() { ["id"] = "Bekukan" } },
        })).ToList();

        var finding = Assert.Single(results);
        Assert.Equal(("b", AnalysisSeverity.Error, "Bekukan"), (finding.Namespace, finding.Severity, finding.SuggestedFix));
        var system = JsonDocument.Parse(setup.Handler.Calls[0].Body).RootElement.GetProperty("system").GetString()!;
        Assert.Contains("Hold → id: Bekukan", system, StringComparison.Ordinal);
        Assert.Contains("Banking app", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_WhenEveryRequestFails_ThrowsTheFirstError()
    {
        var setup = Create(new StubHandler(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limited"}}"""));

        var ex = await Assert.ThrowsAsync<AiRequestException>(() => new TranslationAnalyzerService(setup.Ai).AnalyzeAsync(new AnalysisRequest
        {
            ApplicationContext = "x",
            Items = [new AnalysisItem("a", "Save", "Simpan", "id")],
        }));
        Assert.Contains("rate limited", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clarity_ReviewsSourceStrings_WithSuggestionAndTranslatorNote()
    {
        const string findings = """[{"index":0,"severity":"warning","issue":"Ambiguous: empty or transparent?","suggestion":"Clear all filters","note":"Button that removes every filter","confidence":0.8},{"index":1,"severity":"suggestion","issue":"Needs context","suggestion":"Order","confidence":0.6}]""";
        var setup = Create(new StubHandler(HttpStatusCode.OK, ClaudeReply(findings)));

        var results = await new SourceClarityService(setup.Ai).ReviewAsync(new ClarityRequest
        {
            Items = [new ClarityItem("filters.clear", "Clear"), new ClarityItem("menu.order", "Order"), new ClarityItem("empty", " ")],
            SourceLanguage = "en",
        });

        Assert.Equal(2, results.Count);
        Assert.Equal(("filters.clear", "Clear all filters", "Button that removes every filter"), (results[0].Namespace, results[0].SuggestedSource, results[0].TranslatorNote));
        // A "suggestion" equal to the source is no suggestion.
        Assert.Null(results[1].SuggestedSource);
        Assert.Equal(AnalysisSeverity.Suggestion, results[1].Severity);
        var call = Assert.Single(setup.Handler.Calls);
        Assert.Contains("[1] key=\"menu.order\"", JsonDocument.Parse(call.Body).RootElement.GetProperty("messages")[0].GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    // ───────────────────────── Migration from the old LLM providers ─────────────────────────

    [Fact]
    public void LegacyProviders_MoveToAiIntegration_AndLeaveTheOthers()
    {
        var protector = new SecureStorageService(Path.Combine(_root, "secret.key"));
        var secrets = new SecretService(protector, Path.Combine(_root, "secrets.json"));
        var providersFile = Path.Combine(_root, "providers.json");
        File.WriteAllText(providersFile, JsonSerializer.Serialize(new object[]
        {
            new { Provider = "Google", Options = new { }, Secrets = new { api_key = protector.Protect("google-key") } },
            new { Provider = "Claude", Options = new { endpoint = "https://api.anthropic.com", model = "claude-sonnet", prompt = "Translate casually." }, Secrets = new { api_key = protector.Protect("sk-ant") } },
            new { Provider = "Gemini", Options = new { endpoint = "", model = "gemini-flash-latest", prompt = "" }, Secrets = new { api_key = "" } },
        }));
        var translatePrompt = Path.Combine(_root, "prompts", "translate.md");

        var settings = LegacyAiMigration.Migrate(providersFile, protector, secrets, lastProvider: "Claude", translatePrompt);

        Assert.False(settings.Enabled);
        Assert.Equal("anthropic", settings.Backend);
        Assert.Null(settings.Backends["anthropic"].Endpoint);
        Assert.Equal("claude-sonnet", settings.Backends["anthropic"].Model);
        Assert.Null(settings.Backends["gemini"].Model);
        Assert.Equal("sk-ant", secrets.GetSecret("ai/anthropic/api_key"));
        Assert.Null(secrets.GetSecret("ai/gemini/api_key"));

        var remaining = File.ReadAllText(providersFile);
        Assert.Contains("Google", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("Claude", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("Gemini", remaining, StringComparison.Ordinal);

        var prompt = File.ReadAllText(translatePrompt);
        Assert.StartsWith("Translate casually.", prompt, StringComparison.Ordinal);
        Assert.Contains("{{#context}}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoreMigratesOnce_WhenAiJsonDoesNotExist()
    {
        var seeded = 0;
        var file = Path.Combine(_root, "ai.json");
        AiSettings Seed() { seeded++; return new AiSettings { Backend = "gemini" }; }

        Assert.Equal("gemini", new AiSettingsStore(file, Seed).Load().Backend);
        Assert.Equal("gemini", new AiSettingsStore(file, Seed).Load().Backend);
        Assert.Equal(1, seeded);
    }

    // ───────────────────────── Secret store ─────────────────────────

    [Fact]
    public void Secrets_AreEncryptedAtRest_AndListedByName()
    {
        var file = Path.Combine(_root, "secrets.json");
        var secrets = new SecretService(new SecureStorageService(Path.Combine(_root, "secret.key")), file);

        secrets.SetSecret("ai/anthropic/api_key", "sk-very-secret");
        secrets.SetSecret("mt/deepl/api_key", "deepl-key");
        secrets.SetSecret(SecretKeys.Provider("DeepL", "api_key", Path.Combine(_root, "Project")), "project-key");

        Assert.DoesNotContain("sk-very-secret", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal(["ai/anthropic/api_key", "mt/deepl/api_key"], secrets.Keys().Where(k => !k.StartsWith("project/", StringComparison.Ordinal)));
        Assert.Equal(["mt/deepl/api_key"], secrets.Keys("mt/"));

        // Another process (the CLI) sees the same secrets.
        var other = new SecretService(new SecureStorageService(Path.Combine(_root, "secret.key")), file);
        Assert.Equal("sk-very-secret", other.GetSecret("AI/Anthropic/API_KEY"));

        secrets.SetSecret("ai/anthropic/api_key", "");
        Assert.Null(secrets.GetSecret("ai/anthropic/api_key"));
        Assert.Equal(1, secrets.RemoveAll("project/"));
        Assert.Equal(["mt/deepl/api_key"], other.Keys());
    }

    [Fact]
    public void SecretKeys_HashTheProjectFolder_SoThePathIsNotStored()
    {
        var project = Path.Combine(_root, "MyProject");
        var key = SecretKeys.Provider("DeepL", "api_key", project);

        Assert.Matches("^project/[0-9a-f]{16}/mt/deepl/api_key$", key);
        Assert.DoesNotContain("myproject", key, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(key, SecretKeys.Provider("deepl", "API_KEY", project + Path.DirectorySeparatorChar));
        Assert.Equal("mt/deepl/api_key", SecretKeys.Provider("DeepL", "api_key"));
        Assert.Equal("ai/anthropic/api_key", SecretKeys.Ai("anthropic"));
    }
}

/// <summary>Tests that set process environment variables run one at a time.</summary>
[CollectionDefinition(nameof(EnvironmentVariableTests), DisableParallelization = true)]
public sealed class EnvironmentVariableTests;
