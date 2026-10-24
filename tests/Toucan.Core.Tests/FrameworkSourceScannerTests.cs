using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.Frameworks;
using Toucan.Modules;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class FrameworkSourceScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-m6-").FullName;
    public void Dispose() => Directory.Delete(_root, true);
    private static SourceFileResult Extract(string id, string source, string file = "src/messages.tsx") =>
        new FrameworkSourceScanner(id).Extract(new(file, source, new FrameworkOptions(), default));
    [Theory]
    [InlineData("i18next", "t(\n 'title', { count: 2, ns: 'checkout' }\n)", "title", "checkout")]
    [InlineData("i18next", "t('checkout:title')", "title", "checkout")]
    [InlineData("i18next", "const { t } = useTranslation('checkout', {keyPrefix: 'buttons'}); t('save')", "buttons.save", "checkout")]
    [InlineData("transloco", "service.translate(\n 'title', {name: 'A'}\n)", "title", "main")]
    [InlineData("transloco", "{{ 'title' | transloco }}", "title", "main")]
    [InlineData("transloco", "<div *transloco=\"let t; read: 'checkout'\">{{ t('title') }}</div>", "checkout.title", "main")]
    [InlineData("vue", "<div>{{ $t(\n 'title', {name: 'A'}\n) }}</div>", "title", "main")]
    [InlineData("vue", "<p :title=\"$t('title')\"/>", "title", "main")]
    [InlineData("vue", "<p v-t=\"'title'\"/>", "title", "main")]
    [InlineData("vue", "<i18n-t keypath=\"title\"/>", "title", "main")]
    [InlineData("rails", "I18n.t(\n 'title', count: 2\n)", "title", "main")]
    [InlineData("rails", "<%= t '.title' %>", "users.show.title", "main")]
    [InlineData("formatjs", "intl.formatMessage({\n id: 'title', defaultMessage: 'Title'\n}, {name: 'A'})", "title", "main")]
    [InlineData("formatjs", "<FormattedMessage id=\"title\" defaultMessage=\"Title\"/>", "title", "main")]
    [InlineData("formatjs", "<FormattedMessage id={'title'} defaultMessage=\"Title\"/>", "title", "main")]
    [InlineData("formatjs", "defineMessages({ title: {id:'title',defaultMessage:'Title'} })", "title", "main")]
    public void StaticFrameworkSyntaxExtractsKeysAndPackages(string id, string source, string key, string package)
    {
        var result = Extract(id, source, id == "rails" ? "app/views/users/show.html.erb" : "src/messages.tsx");
        var reference = Assert.Single(result.References); Assert.Equal(key, reference.Key); Assert.Equal(package, reference.Package);
        Assert.True(reference.Line >= 1); Assert.True(reference.Column >= 1); Assert.Empty(result.Diagnostics);
    }
    [Fact]
    public void FallbackListsMultilineLocationsEscapesAndNegativeLiteralsAreParsed()
    {
        var result = Extract("i18next", "// t('comment')\nconst text = \"t('quoted')\";\n/* t('block') */\nt(\n ['checkout:first', 'checkout:sec\\u006fnd'], {count:2}\n);\nconst regex = /t('regex')/;");
        Assert.Equal(new[] { "first", "second" }, result.References.Select(r => r.Key));
        Assert.All(result.References, r => Assert.Equal(5, r.Line));
        Assert.Equal(3, result.References[0].Column); // opening quote after space and '['
        Assert.Empty(result.Diagnostics);
    }
    [Theory]
    [InlineData("vue", "<p v-t=\"dynamicKey\"></p>")]
    [InlineData("transloco", "<p [transloco]=\"dynamicKey\"></p>")]
    [InlineData("i18next", "t?.('title')")]
    [InlineData("i18next", "const {t} = useTranslation(namespaceName); t('title')")]
    [InlineData("rails", "t('title', scope: currentScope)")]
    [InlineData("transloco", "provideTranslocoScope({scope:'todos',alias:'custom'}); t('custom.title')")]
    [InlineData("i18next", "t(prefix + '.title')")]
    [InlineData("i18next", "t(`hello.${name}`)")]
    [InlineData("i18next", "t('title',{ns: currentNamespace})")]
    [InlineData("i18next", "t(['static', dynamic])")]
    [InlineData("formatjs", "intl.formatMessage({id: dynamic})")]
    [InlineData("formatjs", "<FormattedMessage id={dynamic}/>")]
    [InlineData("formatjs", "intl.formatMessage({defaultMessage: 'Generated ID'})")]
    [InlineData("rails", "t(\"hello.#{name}\")")]
    public void DynamicExpressionsRemainUnresolved(string id, string source)
    {
        var result = Extract(id, source);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Code == "source.unresolved");
    }
    [Theory]
    [InlineData("vue", "<template><span>$t('plain-text')</span><div>{{ $t('actual') }}</div></template>")]
    [InlineData("i18next", "const content = <div>t('plain-text'){t('actual')}</div>;")]
    [InlineData("formatjs", "const content = <div>formatMessage({id:'plain-text'})<FormattedMessage id='actual'/></div>;")]
    public void TemplateTextIsNotExecutableSource(string id, string source)
    {
        var result = Extract(id, source, id == "vue" ? "src/component.vue" : "src/component.tsx");
        Assert.Equal("actual", Assert.Single(result.References).Key); Assert.Empty(result.Diagnostics);
    }
    [Theory]
    [InlineData("<ng-container *transloco=\"let t; prefix: 'dashboard'\">{{ t('title') }}</ng-container>", "dashboard.title", "main")]
    [InlineData("<ng-container *transloco=\"let t; scope: 'checkout'\">{{ t('checkout.title') }}</ng-container>", "title", "checkout")]
    [InlineData("<p [title]=\"'title' | transloco\"></p>", "title", "main")]
    [InlineData("<p transloco=\"title\"></p>", "title", "main")]
    public void TranslocoPrefixesScopesAndInputsFollowPublicSyntax(string source, string key, string package)
    {
        var result = Extract("transloco", source, "src/component.html");
        var reference = Assert.Single(result.References); Assert.Equal(key, reference.Key); Assert.Equal(package, reference.Package); Assert.Empty(result.Diagnostics);
    }
    [Theory]
    [InlineData("rails", "t(:title)", "title")]
    [InlineData("rails", "t :title", "title")]
    [InlineData("rails", "I18n.t('title', scope: 'users')", "users.title")]
    [InlineData("vue", "<template><!-- {{ $t('comment') }} --><p>{{ $t('title') }}</p></template>", "title")]
    [InlineData("vue", "<script>const value = t('title');</script><template><p>t('plain')</p></template>", "title")]
    [InlineData("formatjs", "formatMessage({id: 'title', description: {id: 'not-a-message'}})", "title")]
    public void AdditionalStaticSyntaxDoesNotInventMessageIds(string id, string source, string key)
    {
        var result = Extract(id, source, id == "vue" ? "src/component.vue" : "src/messages.tsx");
        Assert.Equal(key, Assert.Single(result.References).Key); Assert.Empty(result.Diagnostics);
    }
    [Fact]
    public async Task LegacyConstructionAndUnreadableFilesHaveExplicitResults()
    {
        File.WriteAllText(Path.Combine(_root, "good.ts"), "t('title')");
        var fallback = new SourceCodeService();
        Assert.Equal(1, (await fallback.ScanAsync(_root)).KeysFound);
        Assert.False(fallback.LastScan!.CanClassifyUnused);
        var locked = Path.Combine(_root, "locked.ts"); File.WriteAllText(locked, "t('locked')");
        using var stream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var partial = await Service().ScanAsync(new SourceScanRequest(_root, "i18next"));
        Assert.False(partial.Complete); Assert.Equal("title", Assert.Single(partial.References).Key);
        Assert.Contains(partial.Diagnostics, d => d.Code == "source.read-failed");
    }
    [Fact]
    public void PathologicalCallsStopAtTheParserWorkLimit()
    {
        var source = string.Concat(Enumerable.Repeat("t(", 30_000));
        var result = Extract("i18next", source);
        Assert.Empty(result.References);
        Assert.Contains(result.Diagnostics, d => d.Code == "source.parser-limit");
    }
    [Fact]
    public async Task EmptyIncludesMeanDefaultScopeAndUnknownOptionsStayVisible()
    {
        File.WriteAllText(Path.Combine(_root, "key.ts"), "t('title')");
        var service = Service();
        Assert.Single((await service.ScanAsync(new SourceScanRequest(_root, "i18next", Includes: []))).References);
        var options = new FrameworkOptions { ExtensionData = new() { ["futureOption"] = System.Text.Json.JsonSerializer.SerializeToElement(true) } };
        var scan = await service.ScanAsync(new SourceScanRequest(_root, "i18next", options));
        Assert.False(scan.CanClassifyUnused); Assert.Contains(scan.Diagnostics, d => d.Code == "source.unsupported-options");
    }
    [Fact]
    public void EscapesAndInterpolatedStringsCannotInventKeysOrCertifyUnused()
    {
        Assert.Equal("title", Assert.Single(Extract("i18next", "t('\\x74\\u{69}tle')").References).Key);
        Assert.Equal("literal\\nkey", Assert.Single(Extract("rails", "t('literal\\nkey')").References).Key);
        var dynamic = Extract("i18next", "const greeting = `Hello ${t('nested')}`; t('known');");
        Assert.Contains(dynamic.Diagnostics, d => d.Code == "source.dynamic-template");
        Assert.Equal("known", Assert.Single(dynamic.References).Key);
    }
    private SourceCodeService Service()
    {
        var scanners = new[] { "generic", "i18next", "transloco", "vue", "rails", "formatjs" }.Select(id => (ISourceScanner)new FrameworkSourceScanner(id));
        return new(scanners, [new I18nextProfile(), new RailsYamlProfile(), new ScriptFrameworkProfile("formatjs", "FormatJS", "formatjs")]);
    }
    [Fact]
    public async Task ProjectScanSeparatesPackagesRespectsScopeAndWithholdsUnusedWhenUnresolved()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src")); Directory.CreateDirectory(Path.Combine(_root, "node_modules"));
        File.WriteAllText(Path.Combine(_root, "src/messages.ts"), "t('checkout:title');");
        File.WriteAllText(Path.Combine(_root, "node_modules/ignored.ts"), "t('ignored');");
        File.WriteAllText(Path.Combine(_root, "src/ignored.js"), "t('ignored');");
        var keys = new[] { new SourceScanKey("main", "title", "main.title", ""), new SourceScanKey("checkout", "title", "checkout.title", "") };
        var service = Service(); var request = new SourceScanRequest(_root, "i18next", Packages: ["main", "checkout"], Includes: ["src/**/*.ts"], ResourceKeys: keys);
        var scan = await service.ScanAsync(request);
        Assert.True(scan.CanClassifyUnused); Assert.Equal(1, scan.FilesScanned);
        Assert.Single(service.FindUsages("checkout.title")); Assert.Empty(service.FindUsages("main.title"));
        Assert.Equal(new[] { "main.title" }, service.GetUnusedKeys(["main.title", "checkout.title"]));
        File.AppendAllText(Path.Combine(_root, "src/messages.ts"), "t(dynamic);");
        Assert.False((await service.ScanAsync(request)).CanClassifyUnused);
        Assert.Empty(service.GetUnusedKeys(["main.title", "checkout.title"]));
    }
    [Fact]
    public async Task CancellationMissingRootsOversizedFilesAndUnknownPackagesAreExplicit()
    {
        var service = Service(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var stopped = await service.ScanAsync(new SourceScanRequest(_root, "i18next"), cancelled.Token);
        Assert.True(stopped.Cancelled); Assert.False(stopped.CanClassifyUnused);
        Assert.False((await service.ScanAsync(new SourceScanRequest(Path.Combine(_root, "missing"), "i18next"))).Complete);
        using (var file = File.Create(Path.Combine(_root, "large.ts"))) file.SetLength(SourceCodeService.MaxFileBytes + 1);
        File.WriteAllText(Path.Combine(_root, "keys.ts"), "t('unknown:title')");
        var scan = await service.ScanAsync(new SourceScanRequest(_root, "i18next", Packages: ["main"]));
        Assert.Contains(scan.Diagnostics, d => d.Code == "source.size-limit"); Assert.Contains(scan.Diagnostics, d => d.Code == "source.unknown-package");
        Assert.False(scan.CanClassifyUnused);
        service.Reset(); Assert.False(service.HasScanData);
    }
    [Fact]
    public async Task GenericFallbackRemainsAvailableButCannotCertifyUnusedKeys()
    {
        File.WriteAllText(Path.Combine(_root, "usage.cs"), "GetString(\"title\"); Localizer[\"other\"];");
        var service = Service(); var result = await service.ScanAsync(new SourceScanRequest(_root));
        Assert.Equal(2, result.References.Count); Assert.False(result.CanClassifyUnused); Assert.True(service.HasScanData);
        Assert.Empty(service.GetUnusedKeys(["unused"]));
    }
}
