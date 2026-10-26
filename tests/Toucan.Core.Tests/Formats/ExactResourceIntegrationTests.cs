using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests.Formats;

public sealed class ExactResourceIntegrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-exact-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);
    private static FileService Files() => new(NullLogger<FileService>.Instance);
    private static ProjectService Service(ITranslationStrategyFactory? factory = null) => new(Files(), FormatTestHost.SaveStrategies,
        factory ?? FormatTestHost.Factory, new ProjectModeResolver(), NullLogger<ProjectService>.Instance, FormatTestHost.Importers);

    public static TheoryData<string, string, string, string> Formats => new()
    {
        { "generic-json", "json", ".json", "{\"title\":\"日本語 — café 😀\"}" },
        { "generic-yaml", "yaml", ".yml", "title: '日本語 — café 😀'\n" },
        { "arb", "arb", ".arb", "{\"@@locale\":\"wrong\",\"@@context\":\"original\",\"title\":\"日本語 — café 😀\",\"@title\":{\"description\":\"keep\"}}" },
        { "prop-files", "java-properties", ".properties", "title=日本語 — café 😀\n" },
        { "resx", "resx", ".resw", "<root><data name=\"title\"><value>日本語 — café 😀</value><comment>keep</comment></data></root>" },
        { "laravel", "laravel-php", ".php", "<?php return ['title' => '日本語 — café 😀'];" }
    };

    private string Project(string framework, string extension, string content)
    {
        var packages = new XElement("translation_packages");
        foreach (var package in new[] { "main", "checkout" })
        {
            var urls = new XElement("translation_urls");
            foreach (var language in new[] { "en-US", "id" })
            {
                var relative = $"{package}/{language}/arbitrary-catalog{extension}";
                var full = Path.Combine(_root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
                urls.Add(new XElement("translation_url", new XElement("path", relative), new XElement("language", language)));
            }
            packages.Add(new XElement("translation_package", new XElement("name", package), urls));
        }
        var root = new XElement("babeledit_project", new XAttribute("version", "1.3"), new XElement("framework", framework), packages);
        var file = Path.Combine(_root, "project.babel");
        new XDocument(root).Save(file);
        return file;
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void DeclaredFilesAndLocalesSurviveMultiplePackagesNativeReopenAndLastEntryDeletion(string framework, string format, string extension, string content)
    {
        var babel = Project(framework, extension, content);
        var source = File.ReadAllBytes(babel);
        var sibling = Path.Combine(_root, "unrelated" + extension);
        File.WriteAllText(sibling, "This unrelated file must never be parsed or overwritten.");
        var siblingBytes = File.ReadAllBytes(sibling);
        var service = Service();
        var loaded = service.LoadProject(_root);
        Assert.Equal(format, loaded.Settings.SaveFormat);
        Assert.Equal(4, loaded.Translations.Count);
        Assert.Equal(4, loaded.Settings.ResourceDocuments.Count);
        Assert.All(loaded.Translations, i =>
        {
            Assert.Contains(i.Language, new[] { "en-US", "id" });
            Assert.Equal("日本語 — café 😀", i.Value);
            Assert.NotNull(i.Identity);
        });
        var identities = loaded.Translations.Select(i => i.Identity!.EntryId).Order().ToArray();
        foreach (var item in loaded.Translations) { item.Value = "Changed"; item.Comment = "Reviewed"; item.IsApproved = true; }
        service.Save(loaded.Settings, [], loaded.Translations);
        File.Move(babel, babel + ".original"); // Reopen must work without an installed vendor or source project.
        var reopened = service.LoadProject(_root);
        Assert.Equal(identities, reopened.Translations.Select(i => i.Identity!.EntryId).Order().ToArray());
        Assert.All(reopened.Translations, i => { Assert.Equal("Changed", i.Value); Assert.Equal("Reviewed", i.Comment); Assert.True(i.IsApproved); });
        Assert.NotEmpty(reopened.Settings.Compatibility);
        service.Save(reopened.Settings, [], []);
        var empty = service.LoadProject(_root);
        Assert.Empty(empty.Translations);
        Assert.Equal(4, empty.Settings.ResourceDocuments.Count);
        Assert.Equal(new[] { "en-US", "id" }, empty.Settings.Languages.Order().ToArray());
        Assert.Equal(source, File.ReadAllBytes(babel + ".original"));
        Assert.Equal(siblingBytes, File.ReadAllBytes(sibling));
        Assert.Equal(7, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Count(f => !f.Contains(Path.DirectorySeparatorChar + ".toucan" + Path.DirectorySeparatorChar)));
        if (format == FormatIds.Arb) Assert.Contains("original", File.ReadAllText(Path.Combine(_root, "main/en-US/arbitrary-catalog.arb")));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void PackageServiceDispatchesExactFormatAndClearsEmptyDocuments(string framework, string format, string extension, string content)
    {
        Project(framework, extension, content);
        var project = Service().LoadProject(_root);
        var service = new PackageService(Files(), FormatTestHost.LoadStrategies, FormatTestHost.SaveStrategies);
        var checkout = project.Settings.ResourceDocuments.Where(d => d.Resource.Package == "checkout")
            .ToDictionary(d => d.Resource.RelativePath, d => File.ReadAllBytes(Path.Combine(_root, d.Resource.RelativePath)));
        var items = service.LoadPackage(project.Settings, "main");
        Assert.Equal(2, items.Count);
        Assert.All(items, i => i.Value = "Package edit");
        service.SavePackage(project.Settings, "main", items);
        Assert.All(service.LoadPackage(project.Settings, "main"), i => Assert.Equal("Package edit", i.Value));
        service.SavePackage(project.Settings, "main", []);
        Assert.Empty(service.LoadPackage(project.Settings, "main"));
        foreach (var (path, bytes) in checkout) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_root, path)));
        Assert.Equal(format, project.Settings.SaveFormat);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void PackageCreationUsesTheSelectedFormatAndKeepsExistingResources(string framework, string format, string extension, string content)
    {
        Project(framework, extension, content);
        var loaded = Service().LoadProject(_root);
        var packages = new PackageService(Files(), FormatTestHost.LoadStrategies, FormatTestHost.SaveStrategies);
        var created = packages.CreatePackage(loaded.Settings, "additional");
        Assert.Equal(2, created.TranslationUrls.Count);
        Assert.All(created.TranslationUrls, u => Assert.True(File.Exists(Path.Combine(_root, u.Path))));
        Assert.Empty(packages.LoadPackage(loaded.Settings, "additional"));
        Assert.All(loaded.Settings.ResourceDocuments, d => Assert.Equal(format, d.Resource.FormatId));
        Assert.Equal(4, Service().LoadProject(_root).Translations.Count);
        packages.RemovePackage(loaded.Settings, "additional");
        Assert.Equal(4, Service().LoadProject(_root).Translations.Count);
        Assert.All(created.TranslationUrls, u => Assert.False(File.Exists(Path.Combine(_root, u.Path))));
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void MissingFilesAndEscapingPathsAreRejectedForEveryExactFormat(string framework, string format, string extension, string content)
    {
        var source = Project(framework, extension, content);
        var xml = XDocument.Load(source);
        var path = xml.Descendants("translation_url").First().Element("path")!;
        path.Value = "missing" + extension;
        xml.Save(source);
        var missing = Assert.Throws<CompatibilityImportException>(() => Service().LoadProject(_root));
        Assert.Contains(missing.Diagnostics, d => d.RelativePath == path.Value && d.Field == "path");
        path.Value = "../outside" + extension;
        xml.Save(source);
        var escaping = Assert.Throws<CompatibilityImportException>(() => Service().LoadProject(_root));
        Assert.Contains(escaping.Diagnostics, d => d.RelativePath == path.Value && d.Field == "path");
        Assert.NotEmpty(format);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("missing-file")]
    [InlineData("duplicate-package")]
    [InlineData("duplicate-language")]
    [InlineData("external")]
    [InlineData("empty-packages")]
    [InlineData("unknown-format")]
    public void InvalidMigrationDoesNotCreateNativeManifest(string problem)
    {
        var file = Project("generic-json", ".json", "{}");
        var xml = XDocument.Load(file);
        var packages = xml.Root!.Element("translation_packages")!;
        var url = packages.Descendants("translation_url").First();
        switch (problem)
        {
            case "version": xml.Root.SetAttributeValue("version", "99"); break;
            case "missing-file": url.Element("path")!.Value = "missing.json"; break;
            case "duplicate-package": packages.Add(new XElement(packages.Elements().First())); break;
            case "duplicate-language": url.Parent!.Add(new XElement(url)); break;
            case "external": url.Element("path")!.Value = "../outside.json"; break;
            case "empty-packages": packages.RemoveNodes(); break;
            case "unknown-format": xml.Root.Element("framework")!.Value = "unknown-format"; break;
        }
        xml.Save(file);
        var before = File.ReadAllBytes(file);
        Assert.ThrowsAny<FormatException>(() => Service().LoadProject(_root));
        Assert.False(File.Exists(Path.Combine(_root, "toucan.tproj")));
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void XmlExternalEntitiesAreProhibited()
    {
        File.WriteAllText(Path.Combine(_root, "project.babel"), "<!DOCTYPE babeledit_project [<!ENTITY x SYSTEM 'file:///must-not-be-read'>]><babeledit_project><framework>&x;</framework></babeledit_project>");
        Assert.Throws<CompatibilityImportException>(() => Service().LoadProject(_root));
    }

    [Fact]
    public void ExactResxPreservesOpaqueResourcesHeadersMetadataAndComments()
    {
        Project("resx", ".resw", "<root><resheader name=\"version\"><value>2.0</value></resheader><metadata name=\"opaque\"><value>Keep metadata</value></metadata><data name=\"icon\" type=\"System.Byte[]\"><value>Opaque bytes</value></data><data name=\"title\"><value>Hello</value><comment>Original comment</comment></data></root>");
        var service = Service();
        var loaded = service.LoadProject(_root);
        Assert.All(loaded.Translations, i => { Assert.Equal("Original comment", i.Comment); i.Value = "Changed"; });
        service.Save(loaded.Settings, [], loaded.Translations);
        service.Save(service.LoadProject(_root).Settings, [], []);
        foreach (var document in loaded.Settings.ResourceDocuments)
        {
            var root = XDocument.Load(Path.Combine(_root, document.Resource.RelativePath)).Root!;
            Assert.Equal("2.0", root.Element("resheader")!.Element("value")!.Value);
            Assert.Equal("Keep metadata", root.Element("metadata")!.Element("value")!.Value);
            Assert.Equal("icon", Assert.Single(root.Elements("data")).Attribute("name")!.Value);
            Assert.Equal("Opaque bytes", root.Element("data")!.Element("value")!.Value);
        }
    }

    [Fact]
    public void UnsupportedExactDialectNeverUsesJsonFallback()
    {
        Project("gettext", ".po", "msgid \"title\"\nmsgstr \"Hello\"");
        var error = Assert.Throws<FormatException>(() => Service().LoadProject(_root));
        Assert.Contains("exact-file", error.Message);
        Assert.Contains(".po", error.Message);
    }

    [Fact]
    public void FailedSecondWriteRestoresAllResourcesAndManifest()
    {
        Project("generic-json", ".json", "{\"title\":\"Original\"}");
        var loaded = Service().LoadProject(_root);
        Service().Save(loaded.Settings, [], loaded.Translations);
        var before = loaded.Settings.ResourceDocuments.Select(d => d.Resource.RelativePath).Append("toucan.tproj")
            .ToDictionary(p => p, p => File.ReadAllBytes(Path.Combine(_root, p)));
        loaded.Translations.ForEach(i => i.Value = "Changed");
        var writer = new FailSecondWrite(FormatTestHost.Factory.GetResourceSaveStrategy(FormatIds.Json)!);
        var factory = new TranslationStrategyFactory([writer], FormatTestHost.LoadStrategies);
        Assert.Throws<IOException>(() => Service(factory).Save(loaded.Settings, [], loaded.Translations));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_root, path)));
        Assert.Null(SaveTransaction.FindInterrupted(_root));
    }

    [Fact]
    public async Task DirectSaveDoesNotMistakeAnUnresolvedJournalForAnActiveTransaction()
    {
        Project("generic-json", ".json", "{\"title\":\"Original\"}");
        var loaded = Service().LoadProject(_root);
        Service().Save(loaded.Settings, [], loaded.Translations);
        var file = Path.Combine(_root, loaded.Settings.ResourceDocuments[0].Resource.RelativePath);
        var original = File.ReadAllBytes(file);
        // A different execution context leaves a durable journal, like an abandoned process.
        await Task.Run(() => SaveTransaction.Begin(_root, [file]));
        loaded.Translations.ForEach(i => i.Value = "Must not be written");
        var error = Assert.Throws<IOException>(() => Service().Save(loaded.Settings, [], loaded.Translations));
        Assert.Contains("interrupted save", error.Message);
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.NotNull(SaveTransaction.FindInterrupted(_root));
        Assert.True(SaveTransaction.FindInterrupted(_root)!.Rollback().Complete);
    }

    [Fact]
    public void PhysicalPathBoundaryRejectsExternalJunctionAndAllowsInternalLink()
    {
        var inside = Directory.CreateDirectory(Path.Combine(_root, "inside")).FullName;
        var outside = Directory.CreateTempSubdirectory("toucan-outside-").FullName;
        try
        {
            Link(Path.Combine(_root, "safe"), inside);
            Link(Path.Combine(_root, "escape"), outside);
            Assert.Equal(ResourceFilePath.Resolve(_root, "inside/new.json"), ResourceFilePath.Resolve(_root, "safe/new.json"));
            Assert.Throws<ArgumentException>(() => ResourceFilePath.Resolve(_root, "escape/new.json"));
            File.WriteAllText(Path.Combine(outside, "catalog.json"), "{}");
            Assert.Throws<ArgumentException>(() => ResourceFilePath.Resolve(_root, "escape/catalog.json"));
        }
        finally
        {
            Directory.Delete(Path.Combine(_root, "escape"));
            Directory.Delete(Path.Combine(_root, "safe"));
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void InternalLinkTargetWithLinkedAncestorStaysInsidePhysicalProject()
    {
        var project = Directory.CreateDirectory(Path.Combine(_root, "project")).FullName;
        Directory.CreateDirectory(Path.Combine(project, "inside"));
        var alias = Path.Combine(_root, "alias");
        var link = Path.Combine(project, "safe");
        Link(alias, project);
        try
        {
            Link(link, Path.Combine(alias, "inside"));
            try
            {
                var destination = ResourceFilePath.Resolve(project, "safe/new.json");
                Assert.Equal(ResourceFilePath.Resolve(project, "inside/new.json"), destination);
                File.WriteAllText(destination, "{}");
                Assert.Equal("{}", File.ReadAllText(Path.Combine(project, "inside", "new.json")));
            }
            finally { Directory.Delete(link); }
        }
        finally { Directory.Delete(alias); }
    }

    private static void Link(string path, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(path, target); return; }
        var start = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class FailSecondWrite(IResourceSaveStrategy writer) : ISaveStrategy, IResourceSaveStrategy
    {
        private int _writes;
        public string FormatId => FormatIds.Json;
        public string DefaultFilePath(string language) => language + ".json";
        public void Save(string path, SaveContext context) => throw new InvalidOperationException("Folder writer must not execute.");
        public Task SaveAsync(string path, SaveContext context) => throw new InvalidOperationException("Folder writer must not execute.");
        public ResourceWriteResult WriteResource(ResourceWriteRequest request) => ++_writes == 2
            ? throw new IOException("Injected second-file failure") : writer.WriteResource(request);
    }
}
