using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Xunit;

namespace Toucan.Core.Tests.Formats;

public sealed class ResourceFidelitySettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-m3-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);
    private static ProjectService Service() => new(new FileService(NullLogger<FileService>.Instance), FormatTestHost.SaveStrategies,
        FormatTestHost.Factory, new ProjectModeResolver(), NullLogger<ProjectService>.Instance, FormatTestHost.Importers);
    private ProjectLoadResult Open(string format, string content, ResourceOptions? options = null, string? dialect = null)
    {
        var extension = format switch { "yaml" => "yml", "arb" => "arb", "java-properties" => "properties", "resx" => "resw", "laravel-php" => "php", _ => "json" };
        var resource = new ResourceDescriptor { Id = "catalog", RelativePath = "unusual." + extension, FormatId = format,
            Package = "main", Language = "en", Options = options ?? new(), Dialect = dialect };
        File.WriteAllText(Path.Combine(_root, resource.RelativePath), content, new UTF8Encoding(false));
        new ProjectSettings { ProjectPath = _root, SaveFormat = format, Languages = ["en"], ResourceDocuments = [new() { Resource = resource }] }.Save();
        return Service().LoadProject(_root);
    }
    private void Save(ProjectLoadResult loaded) => Service().Save(loaded.Settings, [], loaded.Translations);
    private string Text(ProjectLoadResult loaded) => File.ReadAllText(Path.Combine(_root, loaded.Settings.ResourceDocuments[0].Resource.RelativePath));

    [Fact]
    public void JsonNoOpPreservesOrderTypesNullsLiteralKeysAndEmptySparseStructures()
    {
        var loaded = Open("json", """{"z":"Z","a.b":"literal","a":{"b":"nested"},"n":1.25,"flag":true,"nothing":null,"empty":{},"list":[],"sparse":[null,"value",{},[]],"numeric":{"0":"zero","2":"two"}}""");
        Save(loaded);
        using var json = JsonDocument.Parse(Text(loaded));
        Assert.Equal("z", json.RootElement.EnumerateObject().First().Name);
        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("n").ValueKind);
        Assert.Equal(JsonValueKind.True, json.RootElement.GetProperty("flag").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nothing").ValueKind);
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("empty").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("list").ValueKind);
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("numeric").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("sparse")[3].ValueKind);
        Assert.Equal("literal", json.RootElement.GetProperty("a.b").GetString());
        Assert.Equal("nested", json.RootElement.GetProperty("a").GetProperty("b").GetString());
    }

    [Theory]
    [InlineData(ResourceArrayPolicy.Preserve, JsonValueKind.Object)]
    [InlineData(ResourceArrayPolicy.ContiguousIndexes, JsonValueKind.Array)]
    public void NumericObjectsConvertOnlyUnderExplicitContiguousPolicy(ResourceArrayPolicy policy, JsonValueKind expected)
    {
        var loaded = Open("json", """{"contiguous":{"0":"zero","1":"one"},"sparse":{"0":"zero","2":"two"},"empty":{}}""", new() { Arrays = policy });
        Save(loaded);
        using var json = JsonDocument.Parse(Text(loaded));
        Assert.Equal(expected, json.RootElement.GetProperty("contiguous").ValueKind);
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("sparse").ValueKind);
        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("empty").ValueKind);
        Assert.Equal(4, Service().LoadProject(_root).Translations.Count);
    }

    [Fact]
    public void DisablingArraysRejectsActualArraysInsteadOfChangingTheirShape()
    {
        Assert.ThrowsAny<FormatException>(() => Open("json", "{\"list\":[\"value\"]}", new() { Arrays = ResourceArrayPolicy.Disabled }));
        Assert.Contains("[\"value\"]", File.ReadAllText(Path.Combine(_root, "unusual.json")));
    }

    [Fact]
    public void ResourceSettingsOverrideTypedProjectAndLegacySettingsIncludingFalseValues()
    {
        var loaded = Open("json", "{\"z\":\"Z\",\"a\":\"A\",\"empty\":\"\"}",
            new() { Indent = ResourceIndent.Tab, Order = ResourceOrder.Preserve, Newline = ResourceNewline.Lf, OutputEncoding = "UTF-8", SaveEmptyTranslations = true });
        loaded.Settings.ResourceOptions = new() { Indent = ResourceIndent.Space4, Order = ResourceOrder.Alphabetical,
            Newline = ResourceNewline.CrLf, OutputEncoding = "UTF-8 BOM", SaveEmptyTranslations = false };
        loaded.Settings.TextEncoding = "UTF-8 BOM";
        loaded.Settings.LineEnding = "CRLF";
        loaded.Settings.SaveEmptyTranslations = false;
        Save(loaded);
        var bytes = File.ReadAllBytes(Path.Combine(_root, "unusual.json"));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain("\r", Text(loaded));
        Assert.Contains("\n\t", Text(loaded));
        using var json = JsonDocument.Parse(Text(loaded));
        Assert.Equal(new[] { "z", "a", "empty" }, json.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void TypedProjectSettingsDriveNewKeyDelimiterAndEmptyPolicy()
    {
        var loaded = Open("json", "{}");
        loaded.Settings.ResourceOptions = new() { Layout = ResourceLayout.Nested, Delimiter = "::", Indent = ResourceIndent.Space4,
            Order = ResourceOrder.Alphabetical, Newline = ResourceNewline.CrLf, OutputEncoding = "UTF-8 BOM", SaveEmptyTranslations = false };
        loaded.Translations.Add(new() { Language = "en", Namespace = "area::title", Value = "Value" });
        loaded.Translations.Add(new() { Language = "en", Namespace = "empty", Value = "" });
        Save(loaded);
        using var json = JsonDocument.Parse(Text(loaded));
        Assert.Equal("Value", json.RootElement.GetProperty("area").GetProperty("title").GetString());
        Assert.False(json.RootElement.TryGetProperty("empty", out _));
        Assert.Contains("\r\n    ", Text(loaded));
        Assert.True(File.ReadAllBytes(Path.Combine(_root, "unusual.json")).AsSpan().StartsWith(Encoding.UTF8.Preamble));
    }

    [Theory]
    [InlineData(ResourceLayout.Literal)]
    [InlineData(ResourceLayout.Dotted)]
    public void FlatLayoutsKeepNewDottedKeysLiteral(ResourceLayout layout)
    {
        var loaded = Open("json", "{}", new() { Layout = layout });
        loaded.Translations.Add(new() { Language = "en", Namespace = "a.b", Value = "Literal" });
        Save(loaded);
        using var json = JsonDocument.Parse(Text(loaded));
        Assert.Equal("Literal", json.RootElement.GetProperty("a.b").GetString());
    }

    [Theory]
    [InlineData(null, "en.title")]
    [InlineData("generic-yaml", "en.title")]
    [InlineData("rails-yaml", "title")]
    public void OnlyDeclaredRailsDialectUnwrapsLocaleRoots(string? dialect, string key)
    {
        var loaded = Open("yaml", "en:\n  title: Hello\n", dialect: dialect);
        Assert.Equal(key, Assert.Single(loaded.Translations).Namespace);
        Save(loaded);
        var reopened = Service().LoadProject(_root);
        Assert.Equal(key, Assert.Single(reopened.Translations).Namespace);
        Assert.Contains("\"en\":", Text(loaded));
    }

    [Fact]
    public void ExactYamlKeepsLiteralNestedKeysArraysAndOpaqueNullEmptyNodes()
    {
        var loaded = Open("yaml", "\"a.b\": literal\na:\n  b: nested\nnone: null\nempty: {}\nlist: []\nsequence:\n  - first\n  -\n    label: second\n");
        Assert.Equal(4, loaded.Translations.Count);
        Save(loaded);
        var values = Service().LoadProject(_root).Translations.Select(i => i.Value).Order().ToArray();
        Assert.Equal(new[] { "first", "literal", "nested", "second" }, values);
        Assert.Contains("\"none\": null", Text(loaded));
        Assert.Contains("\"empty\": {}", Text(loaded));
        Assert.Contains("\"list\": []", Text(loaded));
    }

    [Theory]
    [InlineData("defaults: &defaults\n  title: Hello\ncopy: *defaults\n")]
    [InlineData("title: !custom Hello\n")]
    [InlineData("title: [one, two]\n")]
    [InlineData("title: Hello\n---\ntitle: Other\n")]
    public void UnsupportedYamlCanBeReviewedButCannotBeOverwritten(string content)
    {
        var loaded = Open("yaml", content);
        Assert.NotEmpty(loaded.Warnings);
        var before = File.ReadAllBytes(Path.Combine(_root, "unusual.yml"));
        Assert.ThrowsAny<FormatException>(() => Save(loaded));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_root, "unusual.yml")));
    }

    [Fact]
    public void ArbDescriptionAndStructuredPlaceholdersMergeWithOpaqueMetadataAndClearExplicitly()
    {
        var loaded = Open("arb", """{"@@locale":"en","@@custom":{"keep":true},"title":"Hello {name}","@title":{"description":"Original","placeholders":{"name":{"type":"String","example":"Ada","custom":42}},"unknown":{"keep":true}},"@orphan":{"keep":true}}""");
        var item = Assert.Single(loaded.Translations);
        Assert.Equal("Original", item.Comment);
        item.Comment = "Edited";
        item.FormatData![ArbFormat.Placeholders] = """{"name":{"type":"String","example":"Grace","custom":42}}""";
        item.Namespace = "renamed";
        Save(loaded);
        using (var json = JsonDocument.Parse(Text(loaded)))
        {
            Assert.False(json.RootElement.TryGetProperty("@title", out _));
            var metadata = json.RootElement.GetProperty("@renamed");
            Assert.Equal("Edited", metadata.GetProperty("description").GetString());
            Assert.Equal("Grace", metadata.GetProperty("placeholders").GetProperty("name").GetProperty("example").GetString());
            Assert.True(metadata.GetProperty("unknown").GetProperty("keep").GetBoolean());
            Assert.True(json.RootElement.GetProperty("@orphan").GetProperty("keep").GetBoolean());
        }
        var reopened = Service().LoadProject(_root);
        var back = Assert.Single(reopened.Translations);
        back.Comment = "";
        back.FormatData![ArbFormat.Placeholders] = "{}";
        Save(reopened);
        using var cleared = JsonDocument.Parse(Text(reopened));
        Assert.False(cleared.RootElement.GetProperty("@renamed").TryGetProperty("description", out _));
        Assert.Empty(cleared.RootElement.GetProperty("@renamed").GetProperty("placeholders").EnumerateObject());
        Save(new ProjectLoadResult { Settings = reopened.Settings, Translations = [] });
        Assert.Contains("@@custom", Text(reopened));
        Assert.DoesNotContain("@renamed", Text(reopened));
    }

    [Fact]
    public void JavaPropertiesKeepsCommentsOrderContinuationsAndEscapedKeys()
    {
        const string content = "# Header\n! Keep note\na\\:b\\=c\\ key = First\\\n    continued\nz=\\u00E9\nblank\n";
        var loaded = Open("java-properties", content, new() { InputEncoding = "UTF-8" });
        Assert.Equal("Firstcontinued", loaded.Translations.Single(i => i.Namespace == "a:b=c key").Value);
        Save(loaded);
        Assert.Equal(content, Text(loaded));
        loaded.Translations.Single(i => i.Namespace == "z").Value = "日本語";
        Save(loaded);
        Assert.Contains("# Header\n! Keep note", Text(loaded));
        Assert.Contains("z=\\u65E5\\u672C\\u8A9E", Text(loaded));
        Assert.Equal("日本語", Service().LoadProject(_root).Translations.Single(i => i.Namespace == "z").Value);
    }

    [Fact]
    public void JavaCommentBackslashesDoNotConsumeTheFollowingProperty()
    {
        const string content = "# A comment ending in \\\ntitle=Keep this property\n";
        var loaded = Open("java-properties", content);
        Assert.Equal("Keep this property", Assert.Single(loaded.Translations).Value);
        Save(loaded);
        Assert.Equal(content, Text(loaded));
    }

    [Theory]
    [InlineData("UTF-8", ResourceUnicodeEscapes.Minimal, "é")]
    [InlineData("UTF-8", ResourceUnicodeEscapes.Always, "\\u00E9")]
    [InlineData("ISO-8859-1", ResourceUnicodeEscapes.Minimal, "é")]
    public void JavaOutputEncodingAndEscapePolicyAreIndependent(string encoding, ResourceUnicodeEscapes escapes, string expected)
    {
        var loaded = Open("java-properties", "key=old\n", new() { InputEncoding = "UTF-8", OutputEncoding = encoding, UnicodeEscapes = escapes, Newline = ResourceNewline.CrLf });
        Assert.Single(loaded.Translations).Value = "é";
        Save(loaded);
        var bytes = File.ReadAllBytes(Path.Combine(_root, "unusual.properties"));
        var text = (encoding == "ISO-8859-1" ? Encoding.Latin1 : Encoding.UTF8).GetString(bytes);
        Assert.Contains("key=" + expected + "\r\n", text);
        Assert.Equal("é", Assert.Single(Service().LoadProject(_root).Translations).Value);
    }

    [Theory]
    [InlineData("json", "{\"title\":\"Original\"}")]
    [InlineData("yaml", "title: Original\n")]
    [InlineData("arb", "{\"title\":\"Original\"}")]
    [InlineData("java-properties", "# Keep header\ntitle=Original\n")]
    public void PackageSavesApplySettingsAndPersistReviewStateAndNewLiteralKeys(string format, string content)
    {
        var loaded = Open(format, content);
        loaded.Settings.ResourceOptions = new() { Layout = ResourceLayout.Literal, OutputEncoding = "UTF-8 BOM", Newline = ResourceNewline.CrLf };
        var packages = new PackageService(new FileService(NullLogger<FileService>.Instance), FormatTestHost.LoadStrategies, FormatTestHost.SaveStrategies, FormatTestHost.Importers);
        var items = packages.LoadPackage(loaded.Settings, "main");
        Assert.Single(items).Comment = "Package review";
        items[0].IsApproved = true;
        items.Add(new() { Language = "en", Namespace = "new.key", Value = "New literal" });
        packages.SavePackage(loaded.Settings, "main", items);
        var reopened = Service().LoadProject(_root);
        var reviewed = reopened.Translations.Single(i => i.Namespace == "title");
        Assert.Equal("Package review", reviewed.Comment);
        Assert.True(reviewed.IsApproved);
        Assert.Single(reopened.Translations.Single(i => i.Namespace == "new.key").Identity!.Path);
        Assert.True(File.ReadAllBytes(Path.Combine(_root, reopened.Settings.ResourceDocuments[0].Resource.RelativePath)).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Contains("\r\n", Text(reopened));
    }

    [Theory]
    [InlineData("|", "first\nsecond\n")]
    [InlineData("|-", "first\nsecond")]
    [InlineData(">", "first second\n")]
    public void YamlBlockScalarsKeepTheirSemanticLineBreaks(string style, string expected)
    {
        var loaded = Open("yaml", $"title: {style}\n  first\n  second\n");
        Assert.Equal(expected, Assert.Single(loaded.Translations).Value);
        Save(loaded);
        Assert.Equal(expected, Assert.Single(Service().LoadProject(_root).Translations).Value);
    }

    [Fact]
    public void YamlNumericEditRemainsAStringAfterASecondEditAndSave()
    {
        var loaded = Open("yaml", "number: 12\n");
        var item = Assert.Single(loaded.Translations);
        item.Value = "text";
        Save(loaded);
        item.Value = "12";
        Save(loaded);
        Assert.Contains("\"number\": \"12\"", Text(loaded));
    }

    [Theory]
    [InlineData("json", "{\"number\":12}")]
    [InlineData("yaml", "number: 12\n")]
    public void RepeatedPackageSavesDoNotRestoreAnObsoleteNumericScalar(string format, string content)
    {
        var loaded = Open(format, content);
        var packages = new PackageService(new FileService(NullLogger<FileService>.Instance), FormatTestHost.LoadStrategies, FormatTestHost.SaveStrategies);
        var items = packages.LoadPackage(loaded.Settings, "main");
        Assert.Single(items).Value = "text";
        packages.SavePackage(loaded.Settings, "main", items);
        items[0].Value = "12";
        packages.SavePackage(loaded.Settings, "main", items);
        Assert.Contains("\"12\"", Text(loaded));
    }

    [Fact]
    public void TypedMoveAndDestinationReassignmentIgnoreStaleVendorKeyAndFileCaches()
    {
        var loaded = Open("json", "{\"old\":\"Value\"}");
        var item = Assert.Single(loaded.Translations);
        var id = item.Identity!.EntryId;
        item.FormatData = new() { ["babel.key"] = "stale", ["json.file"] = "wrong.json" };
        item.Comment = "Keep review";
        item.IsApproved = true;
        var destination = new ResourceDescriptor { Id = "other", RelativePath = "target.json", Language = "fr", FormatId = "json",
            Package = "checkout", SelectorPrefix = "checkout.", Options = new() { Layout = ResourceLayout.Literal } };
        File.WriteAllText(Path.Combine(_root, destination.RelativePath), "{}");
        loaded.Settings.ResourceDocuments.Add(new() { Resource = destination });
        ResourceEntryOperations.Move(item, destination, "new.literal");
        Save(loaded);
        var back = Assert.Single(Service().LoadProject(_root).Translations);
        Assert.Equal("fr", back.Language);
        Assert.Equal(id, back.Identity!.EntryId);
        Assert.Equal("checkout.new.literal", back.Namespace);
        Assert.Equal("Keep review", back.Comment);
        Assert.True(back.IsApproved);
        using var moved = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "target.json")));
        Assert.Equal("Value", moved.RootElement.GetProperty("new.literal").GetString());
        Assert.False(File.Exists(Path.Combine(_root, "wrong.json")));
        var document = loaded.Settings.ResourceDocuments.Single(d => d.Resource.Id == destination.Id);
        document.Resource = document.Resource with { RelativePath = "relocated.json" };
        Save(loaded);
        Assert.Equal(id, Assert.Single(Service().LoadProject(_root).Translations).Identity!.EntryId);
        Assert.True(File.Exists(Path.Combine(_root, "relocated.json")));
    }

    [Fact]
    public void InvalidOutputOverrideLeavesAllOriginalFilesAndManifestUsable()
    {
        var loaded = Open("json", "{\"title\":\"Original\"}");
        var before = File.ReadAllBytes(Path.Combine(_root, "unusual.json"));
        loaded.Settings.ResourceOptions = new() { OutputEncoding = "invalid-codepage" };
        Assert.Single(loaded.Translations).Value = "Changed";
        var error = Assert.ThrowsAny<FormatException>(() => Save(loaded));
        Assert.Contains("outputEncoding", error.Message);
        Assert.Contains("unusual.json", error.Message);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_root, "unusual.json")));
        Assert.Equal("Original", Assert.Single(Service().LoadProject(_root).Translations).Value);
    }

    [Theory]
    [InlineData("json", "{\"title\":\"Original\"}")]
    [InlineData("yaml", "title: Original\n")]
    [InlineData("arb", "{\"title\":\"Original\"}")]
    [InlineData("java-properties", "title=Original\n")]
    [InlineData("resx", "<root><data name=\"title\"><value>Original</value></data></root>")]
    [InlineData("laravel-php", "<?php return ['title' => 'Original'];")]
    public void FileNewlinePreferencesDoNotAlterEmbeddedMessageLineBreaks(string format, string content)
    {
        var loaded = Open(format, content);
        const string value = "First\r\nSecond\nThird\rFourth";
        Assert.Single(loaded.Translations).Value = value;
        loaded.Settings.ResourceOptions = new() { Newline = ResourceNewline.CrLf, OutputEncoding = "UTF-8 BOM" };
        Save(loaded);
        Assert.Equal(value, Assert.Single(Service().LoadProject(_root).Translations).Value);
    }
}
