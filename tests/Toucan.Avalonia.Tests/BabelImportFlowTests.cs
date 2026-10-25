using Avalonia.Headless.XUnit;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class BabelImportFlowTests
{
    [AvaloniaFact]
    public async Task ReopeningTheActiveFolderReadsEditsSavedDuringClose()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("active-reopen", ("en", "{\"title\":\"Hello\"}"));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.PagingController.Data.Single().Translations.Single().Value = "Saved while closing";
        vm.FlushPendingEdits();
        host.Messages.ConfirmAnswer = true;
        await vm.OpenProjectAsync(folder);
        Assert.Equal("Saved while closing", Assert.Single(vm.AllTranslation).Value);
        Assert.False(vm.IsDirty);
        Assert.Contains("Saved while closing", File.ReadAllText(Path.Combine(folder, "en.json")));
    }

    [AvaloniaTheory]
    [InlineData("generic-json", "json", "{\"title\":\"Hello\"}")]
    [InlineData("generic-yaml", "yml", "title: Hello\n")]
    [InlineData("arb", "arb", "{\"title\":\"Hello\"}")]
    [InlineData("prop-files", "properties", "title=Hello\n")]
    [InlineData("resx", "resw", "<root><data name=\"title\"><value>Hello</value></data></root>")]
    [InlineData("laravel", "php", "<?php return ['title' => 'Hello'];")]
    public async Task ExactMigrationUsesProductionCompositionAndSurvivesGuiSaveAndNativeReopen(string framework, string extension, string content)
    {
        using var host = new TestHost();
        var folder = Directory.CreateDirectory(Path.Combine(host.Root, "exact-migration")).FullName;
        var resource = Path.Combine(folder, "catalog." + extension);
        File.WriteAllText(resource, content);
        var source = Path.Combine(folder, "project.babel");
        File.WriteAllText(source, $"<babeledit_project version=\"1.3\"><framework>{framework}</framework><translation_packages><translation_package><name>main</name><translation_urls><translation_url><path>catalog.{extension}</path><language>id</language></translation_url></translation_urls></translation_package></translation_packages></babeledit_project>");
        var before = File.ReadAllBytes(source);
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        Assert.Equal("id", Assert.Single(vm.AllTranslation).Language);
        vm.PagingController.Data.Single().Translations.Single().Value = "Changed";
        vm.FlushPendingEdits();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.Equal(before, File.ReadAllBytes(source));
        File.Delete(source);
        await vm.OpenProjectAsync(folder);
        Assert.Equal("Changed", Assert.Single(vm.AllTranslation).Value);
        Assert.Equal("catalog." + extension, Assert.Single(vm.ProjectSettings!.ResourceDocuments).Resource.RelativePath);
        Assert.NotEmpty(vm.ProjectSettings.Compatibility);
        Assert.False(File.Exists(Path.Combine(folder, "id." + extension)));
    }

    [AvaloniaFact]
    public async Task FailedMigrationLeavesTheActiveWorkspaceAndEditsIntact()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("active", ("en", "{\"title\":\"Hello\"}"));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.PagingController.Data.Single().Translations.Single().Value = "Unsaved";
        vm.FlushPendingEdits();
        var active = vm.ProjectSettings;
        var invalid = Directory.CreateDirectory(Path.Combine(host.Root, "invalid")).FullName;
        File.WriteAllText(Path.Combine(invalid, "project.babel"), "<babeledit_project version=\"99\"><framework>generic-json</framework></babeledit_project>");
        var lifecycle = host.Services.GetRequiredService<IProjectLifecycleService>();
        var result = await lifecycle.OpenProjectAsync(invalid);
        Assert.Equal(ProjectOpenStatus.ManifestInvalid, result.Status);
        Assert.Same(active, lifecycle.CurrentProject);
        Assert.Same(active, vm.ProjectSettings);
        Assert.Equal("Unsaved", Assert.Single(vm.AllTranslation).Value);
        Assert.True(vm.IsDirty);
    }

    [AvaloniaFact]
    public async Task ExcelMetadataOnlyChangesAreAppliedAndSurviveReopen()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("exchange", ("en", "{\"title\":\"Hello\"}"), ("de", "{\"title\":\"Hallo\"}"));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        var exchange = Path.Combine(host.Root, "translated.xlsx");
        ExcelService.Export(exchange,
        [
            new TranslationItem { Language = "en", Namespace = "title", Value = "Hello", Comment = "Reviewed", IsApproved = true },
            new TranslationItem { Language = "de", Namespace = "title", Value = "Hallo", Comment = "Reviewed", IsApproved = false }
        ]);
        host.Dialogs.Files.Enqueue(exchange);
        await vm.ImportExcelCommand.ExecuteAsync(null);
        Assert.True(vm.IsDirty);
        Assert.True(vm.AllTranslation.Single(i => i.Language == "en").IsApproved);
        Assert.False(vm.AllTranslation.Single(i => i.Language == "de").IsApproved);
        Assert.All(vm.AllTranslation, i => Assert.Equal("Reviewed", i.Comment));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        await vm.OpenProjectAsync(folder);
        Assert.True(vm.AllTranslation.Single(i => i.Language == "en").IsApproved);
        Assert.False(vm.AllTranslation.Single(i => i.Language == "de").IsApproved);
        Assert.All(vm.AllTranslation, i => Assert.Equal("Reviewed", i.Comment));
    }

    [AvaloniaFact]
    public async Task BabelCustomFileMappingSurvivesGuiSaveAndReopen()
    {
        using var host = new TestHost();
        var folder = Path.Combine(host.Root, "babel");
        Directory.CreateDirectory(Path.Combine(folder, "custom"));
        File.WriteAllText(Path.Combine(folder, "custom", "english.json"), "{\"title\":\"Hello\",\"empty\":\"\"}");
        File.WriteAllText(Path.Combine(folder, "project.babel"), """
            <babeledit_project><framework>generic-json</framework><primary_language>en</primary_language>
            <languages><language><code>en</code></language></languages>
            <translation_packages><translation_package><name>main</name><translation_urls><translation_url><path>custom/english.json</path><language>en</language></translation_url></translation_urls></translation_package></translation_packages>
            <configuration><format>json</format></configuration></babeledit_project>
            """);
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.PagingController.Data.Single(g => g.Namespace == "title").Translations.Single().Value = "Changed";
        vm.FlushPendingEdits();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.False(File.Exists(Path.Combine(folder, "en.json")));
        Assert.Equal("custom/english.json", Assert.Single(Assert.Single(vm.ProjectSettings!.TranslationPackages).TranslationUrls).Path);
        await vm.OpenProjectAsync(folder);
        Assert.Equal("Changed", vm.AllTranslation.Single(i => i.Namespace == "title").Value);
        Assert.Equal(string.Empty, vm.AllTranslation.Single(i => i.Namespace == "empty").Value);
        var copy = Path.Combine(host.Root, "saved-copy");
        var saved = await host.Services.GetRequiredService<IProjectLifecycleService>().SaveProjectAsAsync(copy);
        Assert.Equal(ProjectSaveStatus.Success, saved.Status);
        await vm.OpenProjectAsync(copy);
        Assert.Equal("Changed", vm.AllTranslation.Single(i => i.Namespace == "title").Value);
        Assert.False(File.Exists(Path.Combine(copy, "en.json")));
    }
}
