using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// Format file-layout conventions (default path, language files, comment sidecar, detection) live on the
/// strategies, so a plugin format gets them without touching any switch statement.
/// </summary>
public sealed class FormatConventionsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-conv-" + Guid.NewGuid().ToString("N"));

    public FormatConventionsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static ISaveStrategy Strategy(string id) => FormatTestHost.Factory.GetSaveStrategy(id)!;

    [Theory]
    [InlineData(FormatIds.Json, "en.json")]
    [InlineData(FormatIds.Yaml, "en.yaml")]
    [InlineData(FormatIds.Po, "en.po")]
    [InlineData(FormatIds.Ini, "en.ini")]
    [InlineData(FormatIds.Arb, "app_en.arb")]
    [InlineData(FormatIds.Csv, "translations.csv")]
    [InlineData(FormatIds.JavaProperties, "en.properties")]
    [InlineData(FormatIds.Xliff, "en.xlf")]
    public void LanguageFilesDefaultsToTheSingleDefaultPath(string formatId, string relative)
    {
        var files = Strategy(formatId).LanguageFiles(_folder, "en");
        Assert.Equal([Path.Combine(_folder, relative)], files);
    }

    [Fact]
    public void LanguageFilesUsesPlatformSeparatorsForNestedDefaults()
    {
        Assert.Equal(Path.Combine(_folder, "res", "values-fr", "strings.xml"), Assert.Single(Strategy(FormatIds.AndroidXml).LanguageFiles(_folder, "fr")));
        Assert.Equal(Path.Combine(_folder, "res", "values", "strings.xml"), Assert.Single(Strategy(FormatIds.AndroidXml).LanguageFiles(_folder, "default")));
        Assert.Equal(Path.Combine(_folder, "de.lproj", "Localizable.strings"), Assert.Single(Strategy(FormatIds.IosStrings).LanguageFiles(_folder, "de")));
        Assert.Equal(Path.Combine(_folder, "Resources.resx"), Assert.Single(Strategy(FormatIds.Resx).LanguageFiles(_folder, "default")));
    }

    [Fact]
    public void NamespacedLanguageFilesIncludeLocalesDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "locales", "en"));
        File.WriteAllText(Path.Combine(_folder, "locales", "en", "app.json"), "{}");

        var files = Strategy(FormatIds.Namespaced).LanguageFiles(_folder, "en");

        Assert.Contains(Path.Combine(_folder, "en.json"), files);
        Assert.Contains(Path.Combine(_folder, "locales", "en", "app.json"), files);
    }

    [Fact]
    public void LaravelLanguageFilesListPhpFilesOrTheExpectedDirectory()
    {
        var strategy = Strategy(FormatIds.LaravelPhp);
        var dir = Path.Combine(_folder, "en");

        Assert.Equal([dir], strategy.LanguageFiles(_folder, "en"));

        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "auth.php"), "<?php return [];");
        Assert.Equal([Path.Combine(dir, "auth.php")], strategy.LanguageFiles(_folder, "en"));
    }

    [Fact]
    public void CommentSidecarBaseFollowsFormatLayout()
    {
        Assert.Equal("en.json", Strategy(FormatIds.Json).CommentSidecarBase("en"));
        Assert.Equal(Path.Combine("en.lproj", "Localizable.strings"), Strategy(FormatIds.IosStrings).CommentSidecarBase("en"));
        Assert.Equal("en", Strategy(FormatIds.LaravelPhp).CommentSidecarBase("en"));
        Assert.Equal("translations.csv", Strategy(FormatIds.Csv).CommentSidecarBase("en"));
    }

    // --- detection ---

    private static string Detect(string folder, params ISaveStrategy[] extra) =>
        new FormatDetector([.. FormatTestHost.SaveStrategies, .. extra]).Detect(folder);

    [Fact]
    public void DetectionPrefersHigherPriorityFormats()
    {
        File.WriteAllText(Path.Combine(_folder, "en.json"), "{}");
        File.WriteAllText(Path.Combine(_folder, "en.yaml"), "a: b");
        File.WriteAllText(Path.Combine(_folder, "app_en.arb"), "{}");

        Assert.Equal(FormatIds.Arb, Detect(_folder));
    }

    [Fact]
    public void DetectionFallsBackToLocalesThenJson()
    {
        Assert.Equal(FormatIds.Json, Detect(_folder));
        Directory.CreateDirectory(Path.Combine(_folder, "locales"));
        Assert.Equal(FormatIds.Namespaced, Detect(_folder));
        Assert.Equal(FormatIds.Json, Detect(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public void DetectionMatchesByFileName()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "res", "values"));
        File.WriteAllText(Path.Combine(_folder, "res", "values", "strings.xml"), "<resources/>");

        Assert.Equal(FormatIds.AndroidXml, Detect(_folder));
    }

    [Fact]
    public void PluginFormatTakesPartInDetection()
    {
        File.WriteAllText(Path.Combine(_folder, "messages.xyz"), "hello");

        Assert.Equal("my-plugin-format", Detect(_folder, new FakePluginFormat("my-plugin-format", new FormatDetection(0, [".xyz"], []))));
    }

    // --- plugin format through the project service ---

    private ProjectService PluginService(FakePluginFormat plugin) => new(
        new FileService(NullLogger<FileService>.Instance), [plugin],
        new TranslationStrategyFactory([plugin], [plugin]), new ProjectModeResolver(), NullLogger<ProjectService>.Instance);

    [Fact]
    public void CreateProjectWithPluginFormatUsesItsStrategyAndDefaultPath()
    {
        var plugin = new FakePluginFormat("my-plugin-format");

        var settings = PluginService(plugin).CreateProject(_folder, ["en", "fr"], "my-plugin-format");

        Assert.Equal(["en", "fr"], plugin.Created);
        var saved = ProjectSettings.LoadFrom(_folder)!;
        Assert.Equal("my-plugin-format", saved.SaveFormat);
        Assert.Equal(["plugin/en.xyz", "plugin/fr.xyz"], saved.TranslationPackages[0].TranslationUrls.Select(u => u.Path));
        Assert.Equal("my-plugin-format", settings.SaveFormat);
    }

    [Fact]
    public void GetDefaultFilePathHonoursCustomPathThenStrategy()
    {
        var service = PluginService(new FakePluginFormat("my-plugin-format"));
        var settings = new ProjectSettings
        {
            ProjectPath = _folder, SaveFormat = "my-plugin-format",
            LanguageFilePaths = new() { ["fr"] = "custom/french.xyz" },
        };

        Assert.Equal("plugin/en.xyz", service.GetDefaultFilePath(settings, "en"));
        Assert.Equal("custom/french.xyz", service.GetDefaultFilePath(settings, "fr"));
        Assert.Equal(Path.Combine(_folder, "custom", "french.xyz"), Assert.Single(service.GetLanguageFiles(settings, "fr")));
        Assert.Equal(Path.Combine(_folder, "plugin", "en.xyz"), Assert.Single(service.GetLanguageFiles(settings, "en")));
    }

    [Fact]
    public void CreateLanguageForUnknownFormatThrowsInsteadOfWritingJson()
    {
        var service = PluginService(new FakePluginFormat("my-plugin-format"));

        Assert.Throws<FormatUnavailableException>(() => service.CreateLanguage(_folder, "en", "other-plugin-format"));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task SaveAsKeepsAPluginFormat()
    {
        var plugin = new FakePluginFormat("my-plugin-format");
        plugin.Created.Clear();
        PluginService(plugin).CreateProject(_folder, ["en"], "my-plugin-format");

        var translationManagement = Substitute.For<ITranslationManagementService>();
        var projectService = PluginService(plugin);
        translationManagement.Translations.Returns(new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "app.title", Value = "Hello" },
        });
        var lifecycle = new ProjectLifecycleService(
            projectService, translationManagement, Substitute.For<IFileWatcherService>(),
            Substitute.For<IValidationPipeline>(), Substitute.For<IAutoSaveService>(), Substitute.For<IDiffMergeEngine>(),
            Substitute.For<IAuditService>(), Substitute.For<IRecentProjectService>(), Substitute.For<ICommentPersistenceService>(),
            new LanguageManagementService(translationManagement, projectService, Substitute.For<IFileWatcherService>(),
                NullLogger<LanguageManagementService>.Instance),
            null, null, NullLogger<ProjectLifecycleService>.Instance);
        Assert.Equal(ProjectOpenStatus.Success, (await lifecycle.OpenProjectAsync(_folder)).Status);

        var target = Path.Combine(_folder, "copy");
        var result = await lifecycle.SaveProjectAsAsync(target);

        Assert.Equal(ProjectSaveStatus.Success, result.Status);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(target, "toucan.tproj")));
        Assert.Equal("my-plugin-format", doc.RootElement.GetProperty("saveFormat").GetString());
        var url = doc.RootElement.GetProperty("translationPackages")[0].GetProperty("translationUrls")[0].GetProperty("path").GetString();
        Assert.Equal("plugin/en.xyz", url);
    }

    private sealed class FakePluginFormat(string id, FormatDetection? detection = null) : ISaveStrategy, ILoadStrategy
    {
        public List<string> Created { get; } = [];
        public string FormatId => id;
        public string DefaultFilePath(string language) => $"plugin/{language}.xyz";
        public FormatDetection? Detection => detection;

        public IEnumerable<TranslationItem> Load(string folder) =>
            [new TranslationItem { Language = "en", Namespace = "app.title", Value = "from-plugin" }];

        public void Save(string path, SaveContext context) => Created.AddRange(context.Languages);
        public Task SaveAsync(string path, SaveContext context) { Save(path, context); return Task.CompletedTask; }
    }
}
