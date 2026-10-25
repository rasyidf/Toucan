using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Extensions;
using Xunit;

namespace Toucan.Core.Tests.Formats;

public sealed class ResourceContractTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-resource-contract-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);
    private static ProjectService Service() => new(new FileService(NullLogger<FileService>.Instance), FormatTestHost.SaveStrategies,
        FormatTestHost.Factory, new ProjectModeResolver(), NullLogger<ProjectService>.Instance, FormatTestHost.Importers);

    [Fact]
    public void SettingsResolveResourceThenProjectThenDetectedThenDefaultsWithoutMutation()
    {
        var resource = new ResourceOptions { SaveEmptyTranslations = false, Layout = ResourceLayout.Literal };
        var project = new ResourceOptions { Layout = ResourceLayout.Nested, Order = ResourceOrder.Preserve };
        var detected = new ResourceOptions { Order = ResourceOrder.Alphabetical, Newline = ResourceNewline.CrLf };
        var defaults = new ResourceOptions { SaveEmptyTranslations = true, Indent = ResourceIndent.Tab, InputEncoding = "UTF-8" };
        var result = ResourceOptions.Resolve(resource, project, detected, defaults);
        Assert.False(result.SaveEmptyTranslations);
        Assert.Equal(ResourceLayout.Literal, result.Layout);
        Assert.Equal(ResourceOrder.Preserve, result.Order);
        Assert.Equal(ResourceNewline.CrLf, result.Newline);
        Assert.Equal(ResourceIndent.Tab, result.Indent);
        Assert.Null(resource.Indent);
        Assert.True(defaults.SaveEmptyTranslations);
    }

    [Fact]
    public void LiteralNestedArrayAndNumericObjectAddressesSurviveEditAndNativeReopen()
    {
        File.WriteAllText(Path.Combine(_root, "en.json"), """{"a.b":"Literal","a":{"b":"Nested"},"items":["Array"],"items[0]":"Literal index","numbers":{"0":"Numeric property"}}""");
        var service = Service();
        var loaded = service.LoadProject(_root);
        Assert.Equal(5, loaded.Translations.Select(i => i.Namespace).Distinct().Count());
        var literal = loaded.Translations.Single(i => i.Value == "Literal");
        var nested = loaded.Translations.Single(i => i.Value == "Nested");
        Assert.NotEqual(literal.Identity!.KeyToken, nested.Identity!.KeyToken);
        var entryId = literal.Identity.EntryId;
        Assert.Equal(entryId, service.LoadProject(_root).Translations.Single(i => i.Value == "Literal").Identity!.EntryId);
        literal.Comment = "Keep note";
        literal.IsApproved = true;
        nested.Namespace = "a.heading";
        literal.Value = "Literal edit";
        service.Save(loaded.Settings, [], loaded.Translations);
        var back = service.LoadProject(_root);
        Assert.Equal(5, back.Translations.Count);
        var restored = back.Translations.Single(i => i.Value == "Literal edit");
        Assert.Equal(entryId, restored.Identity!.EntryId);
        Assert.Equal("Keep note", restored.Comment);
        Assert.True(restored.IsApproved);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "en.json")));
        Assert.Equal("Literal edit", json.RootElement.GetProperty("a.b").GetString());
        Assert.Equal("Nested", json.RootElement.GetProperty("a").GetProperty("heading").GetString());
        Assert.Equal("Array", json.RootElement.GetProperty("items")[0].GetString());
        Assert.Equal("Literal index", json.RootElement.GetProperty("items[0]").GetString());
        Assert.Equal("Numeric property", json.RootElement.GetProperty("numbers").GetProperty("0").GetString());
        Assert.Contains(loaded.Translations.ToNsTree(), n => n.Name == "a.b" && n.Namespace == literal.Namespace);
    }

    [Fact]
    public void MoveDuplicateAndDeletePreserveEntryMetadataAndExplicitDestination()
    {
        var main = new ResourceDescriptor { Id = "main-en", Package = "main", Language = "en", RelativePath = "custom.json", FormatId = FormatIds.Json, SelectorPrefix = "main." };
        var checkout = main with { Id = "checkout-en", Package = "checkout", RelativePath = "checkout.json", SelectorPrefix = "checkout." };
        File.WriteAllText(Path.Combine(_root, main.RelativePath), """{"title":"Main"}""");
        File.WriteAllText(Path.Combine(_root, checkout.RelativePath), """{"title":"Checkout"}""");
        new ProjectSettings { ProjectPath = _root, Languages = ["en"], ResourceDocuments = [new() { Resource = main }, new() { Resource = checkout }] }.Save();
        var service = Service();
        var loaded = service.LoadProject(_root);
        var item = loaded.Translations.Single(i => i.Value == "Main");
        item.Comment = "A note";
        item.IsApproved = true;
        var originalId = item.Identity!.EntryId;
        var copy = ResourceEntryOperations.Copy(item, duplicate: true);
        copy.Namespace = "main.copy";
        Assert.NotEqual(originalId, copy.Identity!.EntryId);
        loaded.Translations.Add(copy);
        ResourceEntryOperations.Move(item, checkout, "moved");
        service.Save(loaded.Settings, [], loaded.Translations);
        var reopened = service.LoadProject(_root);
        Assert.Equal(3, reopened.Translations.Count);
        var moved = reopened.Translations.Single(i => i.Namespace == "checkout.moved");
        Assert.Equal(originalId, moved.Identity!.EntryId);
        Assert.Equal("checkout-en", moved.Identity.ResourceId);
        Assert.True(moved.IsApproved);
        Assert.Equal("A note", moved.Comment);
        reopened.Translations.RemoveAll(i => i.Identity?.ResourceId == main.Id);
        service.Save(reopened.Settings, [], reopened.Translations);
        using var empty = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, main.RelativePath)));
        Assert.Empty(empty.RootElement.EnumerateObject());
        Assert.Equal(2, service.LoadProject(_root).Translations.Count);
    }

    [Fact]
    public void EmptyDocumentAndUnknownCompatibilityDataSurviveNativeRoundTrip()
    {
        File.WriteAllText(Path.Combine(_root, "en.json"), "{}");
        var service = Service();
        var project = service.LoadProject(_root);
        var document = Assert.Single(project.Settings.ResourceDocuments);
        document.Data["opaque.example"] = JsonSerializer.SerializeToElement(new { future = 42 });
        document.ExtensionData = new() { ["futureDocument"] = JsonSerializer.SerializeToElement("preserved") };
        project.Settings.Compatibility["example"] = new CompatibilityEnvelope { Version = 99, Source = "example",
            ExtensionData = new() { ["future"] = JsonSerializer.SerializeToElement(true) } };
        service.Save(project.Settings, [], project.Translations);
        var back = service.LoadProject(_root);
        Assert.Empty(back.Translations);
        Assert.Equal("en.json", Assert.Single(back.Settings.ResourceDocuments).Resource.RelativePath);
        Assert.Equal(42, back.Settings.ResourceDocuments[0].Data["opaque.example"].GetProperty("future").GetInt32());
        Assert.Equal("preserved", back.Settings.ResourceDocuments[0].ExtensionData!["futureDocument"].GetString());
        Assert.True(back.Settings.Compatibility["example"].ExtensionData!["future"].GetBoolean());
        Assert.Equal(["en"], back.Settings.Languages);
    }

    [Fact]
    public void OldBabelManifestMigratesIntoVersionedEnvelopeWithoutLosingUnknownFields()
    {
        File.WriteAllText(Path.Combine(_root, "en.json"), """{"title":"Hello"}""");
        File.WriteAllText(Path.Combine(_root, "toucan.tproj"), """{"saveFormat":"json","futureNative":{"enabled":true},"babelImport":{"sourceFile":"project.babel","jsonLayout":"flat","configuration":{"vendor-new-option":"kept"}},"translationPackages":[{"name":"main","translationUrls":[{"language":"en","path":"en.json"}]}]}""");
        var service = Service();
        var loaded = service.LoadProject(_root);
        service.Save(loaded.Settings, [], loaded.Translations);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "toucan.tproj")));
        Assert.False(manifest.RootElement.TryGetProperty("babelImport", out _));
        Assert.Equal(1, manifest.RootElement.GetProperty("resourceSchemaVersion").GetInt32());
        Assert.True(manifest.RootElement.GetProperty("futureNative").GetProperty("enabled").GetBoolean());
        Assert.Equal("kept", service.LoadProject(_root).Settings.BabelImport!.Configuration["vendor-new-option"]);
    }

    [Fact]
    public void ExactFileRequestNeverFallsBackToFolderPlugin()
    {
        var plugin = new FolderOnly();
        ITranslationStrategyFactory factory = new TranslationStrategyFactory([], [plugin]);
        var result = factory.ReadResource(new ResourceReadRequest(_root, new ResourceDescriptor { FormatId = "folder-only", RelativePath = "custom.txt" }));
        Assert.Null(result.Document);
        Assert.Equal("resource.exact-read-unsupported", Assert.Single(result.Diagnostics).Code);
        Assert.False(plugin.Called);
    }

    [Fact]
    public void ExactWriteRejectsForeignEntriesAndCancellationWithoutTouchingTheFile()
    {
        var file = Path.Combine(_root, "en.json");
        File.WriteAllText(file, "{}");
        var document = new ResourceDocumentState { Resource = new ResourceDescriptor { Id = "target", FormatId = FormatIds.Json, RelativePath = "en.json", Language = "en" } };
        var entry = new TranslationItem { Language = "en", Namespace = "title", Value = "Foreign",
            Identity = new ResourceEntryIdentity { ResourceId = "other", Path = [ResourcePathSegment.Named("title")] } };
        var result = FormatTestHost.Factory.WriteResource(new ResourceWriteRequest(_root, document, [entry]));
        Assert.Equal("resource.foreign-entry", Assert.Single(result.Diagnostics).Code);
        Assert.Equal("{}", File.ReadAllText(file));
        Assert.Throws<OperationCanceledException>(() => FormatTestHost.Factory.ReadResource(new ResourceReadRequest(_root, document.Resource,
            CancellationToken: new CancellationToken(canceled: true))));
    }

    [Fact]
    public void EscapedSelectorsDoNotCollideWithLiteralBackslashesOrWhitespaceKeys()
    {
        File.WriteAllText(Path.Combine(_root, "en.json"), """{"a.b":"Literal","a":{"b":"Nested"},"a\\.b":"Backslash"," ":"Whitespace"}""");
        var service = Service();
        var loaded = service.LoadProject(_root);
        Assert.Equal(4, loaded.Translations.Select(i => i.Namespace).Distinct().Count());
        service.Save(loaded.Settings, [], loaded.Translations);
        var back = service.LoadProject(_root);
        Assert.Equal(4, back.Translations.Count);
        Assert.Equal(4, back.Translations.Select(i => i.Namespace).Distinct().Count());
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "en.json")));
        Assert.Equal("Backslash", json.RootElement.GetProperty("a\\.b").GetString());
        Assert.Equal("Whitespace", json.RootElement.GetProperty(" ").GetString());
    }

    [Fact]
    public void FutureNativeResourceSchemaCannotBeOverwritten()
    {
        new ProjectSettings { ProjectPath = _root, ResourceSchemaVersion = 99 }.Save();
        Assert.Throws<FormatException>(() => Service().LoadProject(_root));
        Assert.Throws<FormatException>(() => Service().Save(new ProjectSettings { ProjectPath = _root, ResourceSchemaVersion = 99 }, [], []));
    }

    [Fact]
    public void SpreadsheetExportUsesRenamedResourceIdentityAndRejectsAmbiguousPaths()
    {
        File.WriteAllText(Path.Combine(_root, "en.json"), """{"a.b":"Literal","a":{"b":"Nested"}}""");
        var loaded = Service().LoadProject(_root);
        var file = Path.Combine(_root, "exchange.xlsx");
        Assert.Throws<FormatException>(() => ExcelService.Export(file, loaded.Translations));
        Assert.False(File.Exists(file));
        var literal = loaded.Translations.Single(i => i.Value == "Literal");
        (literal.FormatData ??= new())["babel.key"] = "stale";
        literal.Namespace = "renamed";
        ExcelService.Export(file, loaded.Translations);
        Assert.Contains(ExcelService.Import(file), i => i.Namespace == "renamed" && i.Value == "Literal");
        Assert.DoesNotContain(ExcelService.Import(file), i => i.Namespace == "stale");
    }

    [Fact]
    public void EditedScalarDoesNotRegainItsOldTypeFromNativeMetadata()
    {
        File.WriteAllText(Path.Combine(_root, "en.json"), """{"number":12}""");
        var service = Service();
        var loaded = service.LoadProject(_root);
        loaded.Translations.Single().Value = "Edited";
        service.Save(loaded.Settings, [], loaded.Translations);
        loaded.Translations.Single().Value = "12";
        service.Save(loaded.Settings, [], loaded.Translations);
        var back = service.LoadProject(_root);
        service.Save(back.Settings, [], back.Translations);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "en.json")));
        Assert.Equal(JsonValueKind.String, json.RootElement.GetProperty("number").ValueKind);
    }

    private sealed class FolderOnly : ILoadStrategy
    {
        public string FormatId => "folder-only";
        public bool Called { get; private set; }
        public IEnumerable<TranslationItem> Load(string folder) { Called = true; return []; }
    }
}
