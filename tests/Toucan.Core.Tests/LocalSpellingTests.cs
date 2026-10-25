using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class LocalSpellingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-spelling-").FullName;
    public void Dispose() => Directory.Delete(_root, true);
    private string Pack(string locale = "en")
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, "input-" + Guid.NewGuid())).FullName;
        File.WriteAllText(Path.Combine(path, locale + ".aff"), "SET UTF-8\nSFX S Y 1\nSFX S 0 s .\n");
        File.WriteAllText(Path.Combine(path, locale + ".dic"), "6\nhello/S\nworld\ncat/S\nHello\nfriend\nthings\n");
        File.WriteAllText(Path.Combine(path, "LICENSE.txt"), "Original Toucan test fixture; MIT. No upstream dictionary data.");
        return path;
    }
    [Fact]
    public async Task AffixesSuggestionsAndLocalProvenanceSurviveRestart()
    {
        var store = Path.Combine(_root, "registry"); var registry = new DictionaryRegistry(store);
        var pack = Assert.Single(await registry.ImportFolderAsync(Pack()));
        Assert.Equal(64, pack.AffixSha256.Length); Assert.Contains("not approved", pack.License);
        var service = new SpellingService(new DictionaryRegistry(store));
        var result = await service.CheckAsync("hello cats wrld", "en", new());
        Assert.True(result.Available, result.Status);
        var issue = Assert.Single(result.Issues); Assert.Equal("wrld", issue.Word); Assert.Contains("world", issue.Suggestions);
        Assert.Equal(11, issue.Offset);
    }
    [Fact]
    public async Task MissingAmbiguousAndExplicitFallbackAreDifferentAvailabilityStates()
    {
        var registry = new DictionaryRegistry(Path.Combine(_root, "registry")); var pack = Assert.Single(await registry.ImportFolderAsync(Pack()));
        var service = new SpellingService(registry);
        Assert.False((await service.CheckAsync("wrong", "en-US", new())).Available);
        var configured = new SpellingSettings { Fallbacks = new() { ["en-us"] = "en" } };
        Assert.True((await service.CheckAsync("hello", "en_US", configured)).Available);
        Assert.Contains("en-us → en", (await service.CheckAsync("hello", "en_US", configured)).Status);
        var other = Pack(); File.AppendAllText(Path.Combine(other, "en.dic"), "extra\n");
        await registry.ImportFolderAsync(other);
        Assert.False((await service.CheckAsync("hello", "en", new())).Available);
        configured = new() { Assignments = new() { ["en"] = pack.Id } };
        Assert.True((await service.CheckAsync("hello", "en", configured)).Available);
        registry.Remove(pack.Id);
        Assert.False((await service.CheckAsync("hello", "en", configured)).Available);
    }
    [Fact]
    public async Task PersonalProjectAndIgnoredWordsStayScoped()
    {
        var registry = new DictionaryRegistry(Path.Combine(_root, "registry")); await registry.ImportFolderAsync(Pack());
        registry.SetUserWords("en", ["Toucan"]);
        var service = new SpellingService(registry);
        var settings = new SpellingSettings { Words = new() { ["en"] = ["projectword"] } };
        var result = await service.CheckAsync("Toucan projectword temporary wrong", "en", settings, new HashSet<string> { "temporary" });
        Assert.Equal("wrong", Assert.Single(result.Issues).Word);
        Assert.Equal(3, (await service.CheckAsync("projectword temporary wrong", "en", new())).Issues.Count);
        Assert.Equal(["Toucan"], new DictionaryRegistry(Path.Combine(_root, "registry")).UserWords("en"));
    }
    [Theory]
    [InlineData("Hello {name} <b>world</b> &amp; {{dynamic}} ${value} %s `code` https://site.test", "Hello world")]
    [InlineData("{count, plural, one {hello friend} other {hello {name} things}}", "hello friend hello things")]
    [InlineData("{kind, select, cat {hello} other {{n, plural, one {cat} other {cats}}}}", "hello cat cats")]
    [InlineData("hello {when, date, long} world", "hello world")]
    [InlineData("hello :name {0:N2} %02d %lld world", "hello world")]
    [InlineData("{this is prose} hello", "this is prose hello")]
    public void NaturalTextExcludesSyntaxButChecksIcuBranchProse(string text, string expected)
    {
        var words = SpellingText.Words(text);
        Assert.Equal(expected, string.Join(' ', words.Select(w => w.Word)));
        Assert.All(words, word => Assert.Equal(word.Word, text.Substring(word.Offset, word.Length)));
    }
    [Fact]
    public async Task RegistrationOnlyImportsSpellingAndPreservesLocaleAliases()
    {
        var input = Pack();
        File.WriteAllText(Path.Combine(input, "dictionaries.xcu"), """<oor:component-data xmlns:oor="urn:openoffice:names:registry"><node oor:name="Dictionaries"><node oor:name="spell"><prop oor:name="Format"><value>DICT_SPELL</value></prop><prop oor:name="Locations"><value>%origin%/en.aff %origin%/en.dic</value></prop><prop oor:name="Locales"><value>en-US en_GB</value></prop></node><node oor:name="other"><prop oor:name="Format"><value>DICT_HYPH</value></prop><prop oor:name="Locations"><value>missing</value></prop></node></node></oor:component-data>""");
        var registry = new DictionaryRegistry(Path.Combine(_root, "registry"));
        var pack = Assert.Single(await registry.ImportFolderAsync(input));
        Assert.Equal(["en-us", "en-gb"], pack.Locales);
    }
    [Theory]
    [InlineData("<!DOCTYPE x [<!ENTITY external SYSTEM 'file:///bad'>]><x>&external;</x>")]
    [InlineData("<node><prop name='Format'><value>DICT_SPELL</value></prop><prop name='Locations'><value>../en.aff ../en.dic</value></prop><prop name='Locales'><value>en</value></prop></node>")]
    public async Task UnsafeRegistrationsCannotInstallPartialPacks(string xml)
    {
        var input = Pack(); File.WriteAllText(Path.Combine(input, "bad.xcu"), xml);
        var registry = new DictionaryRegistry(Path.Combine(_root, "registry"));
        await Assert.ThrowsAnyAsync<Exception>(() => registry.ImportFolderAsync(input));
        Assert.Empty(registry.Packs);
    }
    [Fact]
    public async Task TamperedImportedBytesBecomeUnavailableAndCancellationDoesNotInstall()
    {
        var store = Path.Combine(_root, "registry"); var registry = new DictionaryRegistry(store);
        var pack = Assert.Single(await registry.ImportFolderAsync(Pack()));
        File.AppendAllText(Path.Combine(store, pack.Id, "dictionary.dic"), "tampered\n");
        var result = await new SpellingService(registry).CheckAsync("hello", "en", new());
        Assert.False(result.Available); Assert.Contains("hashes changed", result.Status);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.ImportFolderAsync(Pack("fr"), cts.Token));
        Assert.Single(registry.Packs);
    }
    [Fact]
    public async Task ExplicitLocaleAllowsArbitraryUserDictionaryNames()
    {
        var input = Pack(); File.Move(Path.Combine(input, "en.dic"), Path.Combine(input, "English dictionary.dic"));
        File.Move(Path.Combine(input, "en.aff"), Path.Combine(input, "English dictionary.aff"));
        var registry = new DictionaryRegistry(Path.Combine(_root, "registry"));
        var pack = Assert.Single(await registry.ImportFolderAsync(input, "en_US"));
        Assert.Equal(["en-us"], pack.Locales);
    }
    [Fact]
    public async Task LegacyDictionaryEncodingsWorkWithoutChangingTranslationEncoding()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "encoded")).FullName;
        File.WriteAllText(Path.Combine(folder, "fr.aff"), "SET ISO8859-15\n");
        File.WriteAllBytes(Path.Combine(folder, "fr.dic"), [49, 10, 99, 97, 102, 233, 10]);
        var registry = new DictionaryRegistry(Path.Combine(_root, "registry")); await registry.ImportFolderAsync(folder);
        var result = await new SpellingService(registry).CheckAsync("café", "fr", new());
        Assert.True(result.Available, result.Status); Assert.Empty(result.Issues);
    }
    [Fact]
    public void DenseUnbalancedBracesAreHandledWithoutNestedRescanning()
    {
        Assert.Empty(SpellingText.Words(new string('{', 32000)));
        Assert.Empty(SpellingText.Words(new string('{', 16000) + new string('}', 16000)));
    }
    [Fact]
    public async Task OversizeInputsAndMalformedMetadataAreRejected()
    {
        var input = Pack(); using (var file = File.OpenWrite(Path.Combine(input, "en.aff"))) file.SetLength(4 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<FormatException>(() => new DictionaryRegistry(Path.Combine(_root, "registry")).ImportFolderAsync(input));
        Assert.Throws<FormatException>(() => SpellingText.Words(new string('x', 65537)));
        Assert.Throws<FormatException>(() => ArbPlaceholderMetadata.Validate("{\"name\":{},\"name\":{}}"));
        ArbPlaceholderMetadata.Validate("{\"name\":{\"custom\":{\"keep\":true}}}");
    }
}
