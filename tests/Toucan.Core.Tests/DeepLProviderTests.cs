using System.Net;
using System.Net.Http;
using System.Text;
using Toucan.Core.Models;
using Toucan.Core.Services.Providers;
using Xunit;

namespace Toucan.Core.Tests;

public class DeepLProviderTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Form { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Form = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static PretranslationOptions Options(params (string Key, string Value)[] values) =>
        new() { ProviderOptions = values.ToDictionary(v => v.Key, v => v.Value) };

    [Fact]
    public async Task FreeKey_UsesTheFreeHost_AndTheAuthHeader_NotTheBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"translations":[{"text":"Simpan"}]}""");
        var provider = new DeepLTranslationProvider(new HttpClient(handler));

        var results = (await provider.PretranslateAsync([new PretranslationJob("a", "Save", "en-US", "id")],
            Options(("api_key", "abc:fx"), ("endpoint", "https://api.deepl.com/v2/translate")))).ToList();

        Assert.Equal("https://api-free.deepl.com/v2/translate", handler.Request!.RequestUri!.ToString());
        Assert.Equal("DeepL-Auth-Key abc:fx", handler.Request.Headers.GetValues("Authorization").Single());
        Assert.DoesNotContain("auth_key", handler.Form!, StringComparison.Ordinal);
        Assert.Contains("source_lang=EN", handler.Form!, StringComparison.Ordinal);
        Assert.DoesNotContain("EN-US", handler.Form!, StringComparison.Ordinal);
        Assert.Contains("target_lang=ID", handler.Form!, StringComparison.Ordinal);
        Assert.Equal("Simpan", results.Single().TranslatedValue);
    }

    [Fact]
    public async Task ProKey_KeepsTheConfiguredHost()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"translations":[{"text":"Enregistrer"}]}""");
        var provider = new DeepLTranslationProvider(new HttpClient(handler));

        await provider.PretranslateAsync([new PretranslationJob("a", "Save", "en", "fr-FR")],
            Options(("api_key", "prokey"), ("endpoint", "https://api.deepl.com/v2/translate")));

        Assert.Equal("https://api.deepl.com/v2/translate", handler.Request!.RequestUri!.ToString());
        Assert.Contains("target_lang=FR&", handler.Form! + "&", StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpError_ShowsTheStatusAndDeepLsMessage()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, """{"message":"Wrong endpoint. Use https://api-free.deepl.com"}""");
        var provider = new DeepLTranslationProvider(new HttpClient(handler));

        var results = (await provider.PretranslateAsync([new PretranslationJob("a", "Save", "en", "id")], Options(("api_key", "k")))).ToList();

        Assert.Equal("HTTP 403 Forbidden: Wrong endpoint. Use https://api-free.deepl.com", results.Single().ErrorMessage);
    }

    [Theory]
    [InlineData("en-US", "EN")]
    [InlineData("pt_BR", "PT")]
    [InlineData("id", "ID")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SourceLanguage_IsTheBareCode(string? input, string expected) =>
        Assert.Equal(expected, DeepLTranslationProvider.SourceLanguageCode(input));

    [Theory]
    [InlineData("en", "EN-US")]
    [InlineData("en-US", "EN-US")]
    [InlineData("en-AU", "EN-GB")]
    [InlineData("pt-BR", "PT-BR")]
    [InlineData("pt-PT", "PT-PT")]
    [InlineData("pt", "PT-BR")]
    [InlineData("zh-CN", "ZH-HANS")]
    [InlineData("zh-TW", "ZH-HANT")]
    [InlineData("fr-FR", "FR")]
    [InlineData("de-CH", "DE")]
    [InlineData("id-ID", "ID")]
    public void TargetLanguage_KeepsRegionOnlyWhereDeepLHasVariants(string input, string expected) =>
        Assert.Equal(expected, DeepLTranslationProvider.TargetLanguageCode(input));

    [Theory]
    [InlineData("more", "prefer_more")]
    [InlineData("formal", "prefer_more")]
    [InlineData("less", "prefer_less")]
    [InlineData("informal", "prefer_less")]
    [InlineData("default", "default")]
    public void Formality_UsesThePreferValues(string input, string expected) =>
        Assert.Equal(expected, DeepLTranslationProvider.FormalityValue(input));
}
