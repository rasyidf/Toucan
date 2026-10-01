using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// A project whose format has no registered strategy (e.g. its plugin is missing) must fail loudly
/// instead of being read as JSON, because saving afterwards could overwrite the user's files.
/// </summary>
public sealed class FormatUnavailableTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-fu-" + Guid.NewGuid().ToString("N"));

    public FormatUnavailableTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private ProjectService CreateService() => new(
        new FileService(NullLogger<FileService>.Instance),
        FormatTestHost.SaveStrategies,
        FormatTestHost.Factory,
        new ProjectModeResolver(),
        NullLogger<ProjectService>.Instance);

    private void WriteProject(string formatId)
    {
        new ProjectSettings { ProjectPath = _folder, Languages = ["en"], SaveFormat = formatId }.Save();
        // A JSON file the fallback would happily (and wrongly) load.
        File.WriteAllText(Path.Combine(_folder, "en.json"), """{ "app": { "title": "Hello" } }""");
    }

    [Fact]
    public void LoadingProjectWithUnknownFormatThrows()
    {
        WriteProject("my-plugin-format");

        var ex = Assert.Throws<FormatUnavailableException>(() => CreateService().LoadProject(_folder));

        Assert.Equal("my-plugin-format", ex.FormatId);
        Assert.Contains("my-plugin-format", ex.Message);
    }

    [Fact]
    public void FailedLoadLeavesProjectFilesUntouched()
    {
        WriteProject("my-plugin-format");
        var before = File.ReadAllText(Path.Combine(_folder, "toucan.tproj"));

        Assert.Throws<FormatUnavailableException>(() => CreateService().LoadProject(_folder));

        Assert.Equal(before, File.ReadAllText(Path.Combine(_folder, "toucan.tproj")));
        Assert.Equal("""{ "app": { "title": "Hello" } }""", File.ReadAllText(Path.Combine(_folder, "en.json")));
    }

    [Fact]
    public void RegisteringTheFormatMakesTheProjectLoadable()
    {
        WriteProject("my-plugin-format");
        var plugin = new FakePluginFormat("my-plugin-format");
        var factory = new TranslationStrategyFactory([], [plugin]);
        var service = new ProjectService(
            new FileService(NullLogger<FileService>.Instance), [], factory,
            new ProjectModeResolver(), NullLogger<ProjectService>.Instance);

        var result = service.LoadProject(_folder);

        Assert.Equal("my-plugin-format", result.Settings.SaveFormat);
        Assert.Equal("from-plugin", Assert.Single(result.Translations).Value);
    }

    [Fact]
    public void BuiltInFormatWithoutLoaderKeepsLegacyJsonFallback()
    {
        // INI is save-only today (quirk 5). Pinned so that tightening the plugin case does not also block it.
        WriteProject(FormatIds.Ini);

        var result = CreateService().LoadProject(_folder);

        Assert.Contains(result.Translations, t => t.Namespace == "app.title");
    }

    [Fact]
    public async Task LifecycleReportsFormatUnavailableWithoutOpeningTheProject()
    {
        WriteProject("my-plugin-format");
        var translationManagement = Substitute.For<ITranslationManagementService>();
        var projectService = CreateService();
        var lifecycle = new ProjectLifecycleService(
            projectService, translationManagement, Substitute.For<IFileWatcherService>(),
            Substitute.For<IValidationPipeline>(), Substitute.For<IAutoSaveService>(), Substitute.For<IDiffMergeEngine>(),
            Substitute.For<IAuditService>(), Substitute.For<IRecentProjectService>(), Substitute.For<ICommentPersistenceService>(),
            new LanguageManagementService(translationManagement, projectService, Substitute.For<IFileWatcherService>(),
                NullLogger<LanguageManagementService>.Instance),
            null, null, NullLogger<ProjectLifecycleService>.Instance);

        var result = await lifecycle.OpenProjectAsync(_folder);

        Assert.Equal(ProjectOpenStatus.FormatUnavailable, result.Status);
        Assert.Contains("my-plugin-format", result.ErrorMessage);
        Assert.False(lifecycle.IsProjectOpen);
        translationManagement.DidNotReceiveWithAnyArgs().Initialize(default!);
    }

    private sealed class FakePluginFormat(string id) : ILoadStrategy
    {
        public string FormatId => id;
        public IEnumerable<TranslationItem> Load(string folder) =>
            [new TranslationItem { Language = "en", Namespace = "app.title", Value = "from-plugin" }];
    }
}
