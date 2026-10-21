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
