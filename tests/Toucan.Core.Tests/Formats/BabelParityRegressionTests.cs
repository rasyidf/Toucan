using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;
using Toucan.Extensions;
using Xunit;

namespace Toucan.Core.Tests.Formats;

public sealed class BabelParityRegressionTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("toucan-babel-").FullName;
    private static ProjectService Service() => new(new FileService(NullLogger<FileService>.Instance),
        FormatTestHost.SaveStrategies, FormatTestHost.Factory, new ProjectModeResolver(), NullLogger<ProjectService>.Instance);
    public void Dispose() => Directory.Delete(_folder, true);

    private static string Root
    {
        get
        {
            var path = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(path, "Toucan.CrossPlatform.slnx")))
                path = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Repository root not found.");
            return path;
        }
    }

    public static TheoryData<string> Fixtures() => new(Directory.GetDirectories(Path.Combine(Root, "samples", "babeledit-parity"))
        .Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToArray());

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void CorpusSurvivesNativeSaveAndReopen(string fixture)
    {
        var source = Path.Combine(Root, "samples", "babeledit-parity", fixture);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_folder, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var babel = Path.Combine(_folder, "project.babel");
        var beforeBabel = File.ReadAllBytes(babel);
        var service = Service();
        var loaded = service.LoadProject(_folder);
        var values = loaded.Translations.ToDictionary(i => (i.Language, i.Namespace), i => i.Value);
        Assert.NotEmpty(values);
        Assert.Equal(2, loaded.Translations.Count(i => i.Namespace.EndsWith("unicode", StringComparison.Ordinal) && i.Value == "日本語 — café 😀"));
        Assert.Equal(2, loaded.Translations.Count(i => i.Namespace.EndsWith("empty", StringComparison.Ordinal) && i.Value.Length == 0));
        if (fixture == "12-rails-yaml") Assert.Equal(18, values.Count);
        if (fixture == "18-yaml-arrays") Assert.Equal(22, values.Count);
        service.Save(loaded.Settings, loaded.Translations.ToNsTree().ToList(), loaded.Translations);
        var reopened = service.LoadProject(_folder).Translations.ToDictionary(i => (i.Language, i.Namespace), i => i.Value);
        Assert.Equal(values.OrderBy(p => p.Key), reopened.OrderBy(p => p.Key));
        Assert.Equal(beforeBabel, File.ReadAllBytes(babel));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProjectSettingControlsEmptyValuesButKeepsSpaces(bool keep)
    {
        var items = new List<TranslationItem> { new() { Language = "en", Namespace = "empty", Value = "" }, new() { Language = "en", Namespace = "space", Value = " " } };
        var settings = new ProjectSettings { ProjectPath = _folder, SaveFormat = FormatIds.Json, Languages = ["en"], SaveEmptyTranslations = keep };
        Service().Save(settings, [], items);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "en.json")));
        Assert.Equal(keep, json.RootElement.TryGetProperty("empty", out _));
        Assert.Equal(" ", json.RootElement.GetProperty("space").GetString());
    }

    [Theory]
    [InlineData("json")]
    [InlineData("arb")]
    [InlineData("yaml")]
    [InlineData("java-properties")]
    [InlineData("laravel-php")]
    [InlineData("resx")]
    [InlineData("po")]
    [InlineData("ios-strings")]
    public void FormatWritersKeepEmptyByDefault(string format)
    {
        var key = format == FormatIds.LaravelPhp ? "messages.empty" : "empty";
        var items = new List<TranslationItem> { new() { Language = "en", Namespace = key, Value = "" } };
        FormatTestHost.Factory.GetSaveStrategy(format)!.Save(_folder, new SaveContext
        {
            LanguageDictionary = items.ToLanguageDictionary(), Languages = ["en"], NsTreeItems = items.ToNsTree().ToList()
        });
        Assert.Contains(FormatTestHost.Factory.GetLoadStrategy(format)!.Load(_folder), i => i.Namespace == key && i.Value.Length == 0);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("arb")]
    [InlineData("yaml")]
    [InlineData("java-properties")]
    [InlineData("laravel-php")]
    [InlineData("resx")]
    [InlineData("po")]
    [InlineData("ios-strings")]
    public void OptingOutRemovesTheLastValueInsteadOfLeavingOldTextOnDisk(string format)
    {
        var key = format == FormatIds.LaravelPhp ? "messages.empty" : "empty";
        var items = new List<TranslationItem> { new() { Language = "en", Namespace = key, Value = "Previous" } };
        var writer = FormatTestHost.Factory.GetSaveStrategy(format)!;
        writer.Save(_folder, new SaveContext { LanguageDictionary = items.ToLanguageDictionary(), Languages = ["en"], NsTreeItems = [] });
        var loaded = FormatTestHost.Factory.GetLoadStrategy(format)!.Load(_folder).ToList();
        Assert.NotEmpty(loaded);
        loaded.ForEach(i => i.Value = "");
        writer.Save(_folder, new SaveContext { SaveEmptyTranslations = false, LanguageDictionary = loaded.ToLanguageDictionary(), Languages = ["en"], NsTreeItems = [] });
        Assert.DoesNotContain(FormatTestHost.Factory.GetLoadStrategy(format)!.Load(_folder), i => i.Namespace == key);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JavaPreservesUtf8AndLatin1Files(bool utf8)
    {
        var file = Path.Combine(_folder, "messages_en.properties");
        File.WriteAllText(file, "word=café\n", utf8 ? new UTF8Encoding(false) : Encoding.Latin1);
        var items = new JavaPropertiesLoadStrategy().Load(_folder).ToList();
        Assert.Equal("café", Assert.Single(items).Value);
        Assert.Equal("en", items[0].Language);
        new JavaPropertiesSaveStrategy(new FileService(NullLogger<FileService>.Instance)).Save(_folder,
            new SaveContext { Languages = ["en"], NsTreeItems = [], LanguageDictionary = items.ToLanguageDictionary() });
        Assert.False(File.Exists(Path.Combine(_folder, "en.properties")));
        Assert.Equal("café", Assert.Single(new JavaPropertiesLoadStrategy().Load(_folder)).Value);
        if (utf8) Assert.Contains("café", File.ReadAllText(file));
    }

    [Fact]
    public void RailsAndSequenceOfMappingsKeepFileLayout()
    {
        var file = Path.Combine(_folder, "en.yml");
        File.WriteAllText(file, "en:\n  pages:\n    - title: First\n      subtitle: More\n    - title: Second\n  caption: Tail\n");
        var loader = new YamlLoadStrategy();
        var items = loader.Load(_folder).ToList();
        Assert.Equal("First", items.Single(i => i.Namespace == "pages[0].title").Value);
        Assert.Equal("More", items.Single(i => i.Namespace == "pages[0].subtitle").Value);
        Assert.Equal("Second", items.Single(i => i.Namespace == "pages[1].title").Value);
        Assert.Equal("Tail", items.Single(i => i.Namespace == "caption").Value);
        new YamlSaveStrategy(new FileService(NullLogger<FileService>.Instance)).Save(_folder,
            new SaveContext { Languages = ["en"], NsTreeItems = [], LanguageDictionary = items.ToLanguageDictionary() });
        Assert.False(File.Exists(Path.Combine(_folder, "en.yaml")));
        Assert.StartsWith("en:", string.Join('\n', File.ReadAllLines(file).Where(l => !l.StartsWith('#') && l != "---")));
        Assert.Equal(items.Select(i => (i.Namespace, i.Value)).OrderBy(i => i.Namespace), loader.Load(_folder).Select(i => (i.Namespace, i.Value)).OrderBy(i => i.Namespace));
    }

    [Fact]
    public void NativeBabelImportRetainsMetadataCustomPathsAndKeyRenames()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "custom"));
        File.WriteAllText(Path.Combine(_folder, "custom", "english.json"), "{\"app.title\":\"Hello\"}");
        var xml = """
            <babeledit_project><framework>generic-json</framework><primary_language>en</primary_language>
            <languages><language><code>en</code></language></languages>
            <folder_node><children><package_node><name>main</name><children><folder_node><name>app</name><children>
            <concept_node><name>title</name><description>Heading</description><comment>Review me</comment><translations><translation><language>en</language><approved>true</approved></translation></translations></concept_node>
            </children></folder_node></children></package_node></children></folder_node>
            <translation_packages><translation_package><name>main</name><translation_urls><translation_url><path>custom/english.json</path><language>en</language></translation_url></translation_urls></translation_package></translation_packages>
            <editor_configuration><save_empty_translations>true</save_empty_translations><copy_templates><copy_template>t('%1')</copy_template></copy_templates></editor_configuration>
            <configuration><format>json</format><support_arrays>true</support_arrays></configuration></babeledit_project>
            """;
        File.WriteAllText(Path.Combine(_folder, "project.babel"), xml);
        var service = Service();
        var project = service.LoadProject(_folder);
        var item = Assert.Single(project.Translations);
        Assert.True(item.IsApproved);
        Assert.Equal("Review me", item.Comment);
        Assert.Equal("Heading", item.FormatData!["babel.description"]);
        Assert.Equal("t('%1')", Assert.Single(project.Settings.CopyTemplates!));
        item.Namespace = "app.heading";
        item.Comment = "Updated";
        item.IsApproved = false;
        service.Save(project.Settings, [], project.Translations);
        Assert.False(File.Exists(Path.Combine(_folder, "en.json")));
        var back = Assert.Single(service.LoadProject(_folder).Translations);
        Assert.Equal("app.heading", back.Namespace);
        Assert.Equal("Updated", back.Comment);
        Assert.False(back.IsApproved);
        Assert.Contains("app.heading", File.ReadAllText(Path.Combine(_folder, "custom", "english.json")));
    }

    [Fact]
    public void BabelImportRejectsEscapingResourcePath()
    {
        File.WriteAllText(Path.Combine(_folder, "project.babel"), "<babeledit_project><framework>generic-json</framework><translation_packages><translation_package><translation_urls><translation_url><path>../outside.json</path><language>en</language></translation_url></translation_urls></translation_package></translation_packages></babeledit_project>");
        Assert.Throws<FormatException>(() => Service().LoadProject(_folder));
    }

    [Fact]
    public void DeclaredJavaEncodingWinsOverAmbiguousUtf8Bytes()
    {
        File.WriteAllBytes(Path.Combine(_folder, "en.properties"), [.. Encoding.ASCII.GetBytes("word="), 0xc3, 0xa9]);
        File.WriteAllText(Path.Combine(_folder, "project.babel"), "<babeledit_project><framework>prop-files</framework><configuration><encoding>ISO-8859-1</encoding></configuration></babeledit_project>");
        var project = Service().LoadProject(_folder);
        Assert.Equal("Ã©", Assert.Single(project.Translations).Value);
        Assert.Equal("ISO-8859-1", project.Settings.InputEncoding);
    }

    [Fact]
    public void JavaUtf8SurvivesExplicitLineEndingConversion()
    {
        File.WriteAllText(Path.Combine(_folder, "messages_en.properties"), "word=日本語 — café 😀\n", new UTF8Encoding(false));
        var project = Service().LoadProject(_folder);
        project.Settings.LineEnding = "CRLF";
        Service().Save(project.Settings, [], project.Translations);
        Assert.Contains("\r\n", File.ReadAllText(Path.Combine(_folder, "messages_en.properties")));
        Assert.Equal("日本語 — café 😀", Assert.Single(Service().LoadProject(_folder).Translations).Value);
    }

    [Fact]
    public void NativeJsonPackagesKeepIdenticalKeysInSeparateFiles()
    {
        foreach (var package in new[] { "main", "checkout" })
        {
            Directory.CreateDirectory(Path.Combine(_folder, package));
            File.WriteAllText(Path.Combine(_folder, package, "en.json"), "{\"title\":\"" + package + "\"}");
        }
        File.WriteAllText(Path.Combine(_folder, "project.babel"), """
            <babeledit_project><framework>generic-json</framework><languages><language><code>en</code></language></languages>
            <translation_packages>
            <translation_package><name>main</name><translation_urls><translation_url><path>main/en.json</path><language>en</language></translation_url></translation_urls></translation_package>
            <translation_package><name>checkout</name><translation_urls><translation_url><path>checkout/en.json</path><language>en</language></translation_url></translation_urls></translation_package>
            </translation_packages><configuration><format>json</format></configuration></babeledit_project>
            """);
        var service = Service();
        var project = service.LoadProject(_folder);
        Assert.Equal(new[] { "checkout.title", "main.title" }, project.Translations.Select(i => i.Namespace).Order().ToArray());
        service.Save(project.Settings, [], project.Translations);
        var back = service.LoadProject(_folder);
        Assert.Equal(project.Translations.Select(i => (i.Namespace, i.Value)).Order(), back.Translations.Select(i => (i.Namespace, i.Value)).Order());
        Assert.False(File.Exists(Path.Combine(_folder, "en.json")));
    }

    [Theory]
    [InlineData("rails")]
    [InlineData("java")]
    public void NewKeysJoinTheExistingSourceFile(string kind)
    {
        var file = kind == "rails" ? "en.yml" : "messages_en.properties";
        File.WriteAllText(Path.Combine(_folder, file), kind == "rails" ? "en:\n  title: Old\n" : "title=Old\n");
        var service = Service();
        var project = service.LoadProject(_folder);
        project.Translations.Add(new TranslationItem { Language = "en", Namespace = "new", Value = "New" });
        service.Save(project.Settings, [], project.Translations);
        Assert.False(File.Exists(Path.Combine(_folder, kind == "rails" ? "en.yaml" : "en.properties")));
        Assert.Equal("New", service.LoadProject(_folder).Translations.Single(i => i.Namespace == "new").Value);
    }

    [Fact]
    public void ReviewStateSurvivesLanguageAliases()
    {
        File.WriteAllText(Path.Combine(_folder, "en-US.json"), "{\"title\":\"Hello\"}");
        var service = Service();
        var project = service.LoadProject(_folder);
        project.Settings.LanguageAliases = new() { ["en-US"] = "en" };
        var item = Assert.Single(project.Translations);
        item.Language = "en";
        item.Comment = "Reviewed";
        item.IsApproved = true;
        service.Save(project.Settings, [], project.Translations);
        var reopened = Assert.Single(service.LoadProject(_folder).Translations);
        Assert.Equal("en", reopened.Language);
        Assert.True(reopened.IsApproved);
        Assert.Equal("Reviewed", reopened.Comment);
    }
}
