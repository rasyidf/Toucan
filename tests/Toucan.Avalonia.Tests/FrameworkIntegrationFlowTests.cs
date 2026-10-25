using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class FrameworkIntegrationFlowTests
{
    private static string CreateCatalog(TestHost host, bool emptyTarget = false)
    {
        var folder = Directory.CreateDirectory(Path.Combine(host.Root, "catalog")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "extracted")); Directory.CreateDirectory(Path.Combine(folder, "locales"));
        File.WriteAllText(Path.Combine(folder, "extracted/en.json"), """{"title":{"defaultMessage":"Hello {name}","description":"Greeting","custom":42}}""");
        File.WriteAllText(Path.Combine(folder, "locales/de.json"), emptyTarget ? "{}" : """{"title":"Hallo {name}"}""");
        return folder;
    }
    [AvaloniaFact]
    public async Task ProfileImportValidatesBeforePersistingAndKeepsExistingProjectsIntact()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel();
        var active = host.CreateJsonProject("active-profile", ("en", "{\"title\":\"Active\"}"));
        await vm.OpenProjectAsync(active);
        var folder = Directory.CreateDirectory(Path.Combine(host.Root, "profile-import")).FullName;
        foreach (var lang in new[] { "en", "de" })
        {
            Directory.CreateDirectory(Path.Combine(folder, "locales", lang));
            File.WriteAllText(Path.Combine(folder, "locales", lang, "common.json"), "{\"title\":\"" + lang + "\"}");
        }
        var profiles = host.Services.GetServices<IFrameworkProfile>().ToList();
        var import = new Toucan.Avalonia.ViewModels.ImportProjectViewModel(profiles, host.Dialogs) { Folder = folder,
            SelectedProfile = profiles.Single(p => p.Id == "i18next") };
        host.Dialogs.ImportProjectResult = import; await vm.ImportProjectCommand.ExecuteAsync(null);
        Assert.Equal("i18next", vm.ProjectSettings!.Framework);
        Assert.Equal(2, vm.ProjectSettings.ResourceDocuments.Count);
        Assert.All(vm.ProjectSettings.ResourceDocuments, d => { Assert.Equal("json", d.Resource.FormatId); Assert.Equal("common", d.Resource.Package); });
        var settingsBefore = File.ReadAllBytes(Path.Combine(folder, "toucan.tproj"));
        import.SelectedProfile = profiles.Single(p => p.Id == "vue-json"); // stored native settings take priority
        await vm.ImportProjectCommand.ExecuteAsync(null);
        Assert.Equal(settingsBefore, File.ReadAllBytes(Path.Combine(folder, "toucan.tproj")));
        var bad = CreateCatalog(host);
        File.WriteAllText(Path.Combine(bad, "extracted/en.json"), "{\"title\":{\"defaultMessage\":[]}}");
        host.Dialogs.ImportProjectResult = new(profiles, host.Dialogs) { Folder = bad, SelectedProfile = profiles.Single(p => p.Id == "formatjs") };
        await vm.ImportProjectCommand.ExecuteAsync(null);
        Assert.Equal(folder, vm.CurrentPath); Assert.False(File.Exists(Path.Combine(bad, "toucan.tproj")));
        Assert.Contains(host.Messages.Shown, m => m.Contains("Project import failed", StringComparison.Ordinal));
    }
    [AvaloniaFact]
    public async Task GeneratedSourceCannotBeEditedByEditorBulkWorkspaceOrStructuralCommands()
    {
        using var host = new TestHost(); var folder = CreateCatalog(host); var original = File.ReadAllBytes(Path.Combine(folder, "extracted/en.json"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        Assert.Equal("formatjs", vm.ProjectSettings!.Framework);
        var source = vm.PagingController.Data.SelectMany(g => g.Translations).Single(i => i.Language == "en");
        Assert.True(source.IsResourceReadOnly); source.Value = "Forbidden"; source.Comment = "Forbidden"; source.IsApproved = true; vm.FlushPendingEdits();
        Assert.Equal("Hello {name}", source.Model.Value); Assert.Equal("Greeting", source.Model.Comment); Assert.False(source.Model.IsApproved);
        Assert.False(vm.CanChangeKeys);
        Assert.NotNull(vm.RenameItemCore(new() { Namespace = source.Namespace, Name = "title" }, "renamed"));
        vm.DeleteNamespaces([source.Namespace]); await vm.CreateNewItemAsync("new"); Assert.Equal(2, vm.AllTranslation.Count);
        var snapshot = vm.BuildWorkspaceSnapshot();
        var result = vm.ApplyWorkspaceEdit("test", new WorkspaceEdit().SetValue(source.Namespace, "en", "Forbidden"));
        Assert.False(result.Applied);
        vm.SelectedKeys.Add(source.Namespace); vm.BulkApproveCommand.Execute(null);
        Assert.False(vm.AllTranslation.Single(i => i.Language == "en").IsApproved);
        var target = vm.PagingController.Data.SelectMany(g => g.Translations).Single(i => i.Language == "de");
        target.Value = "Guten Tag {name}"; vm.FlushPendingEdits(); await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, "extracted/en.json")));
        await vm.OpenProjectAsync(folder);
        Assert.False(vm.AllTranslation.Single(i => i.Language == "en").Permissions.EditValues);
        Assert.Equal("Guten Tag {name}", vm.AllTranslation.Single(i => i.Language == "de").Value);
        var copy = Path.Combine(host.Root, "catalog-copy");
        Assert.Equal(ProjectSaveStatus.Success, (await host.Services.GetRequiredService<IProjectLifecycleService>().SaveProjectAsAsync(copy)).Status);
        await vm.OpenProjectAsync(copy);
        Assert.False(vm.AllTranslation.Single(i => i.Language == "en").Permissions.EditValues);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(copy, "extracted/en.json")));
        Assert.False(host.Services.GetRequiredService<Toucan.Core.Commands.ICommandRegistry>().GetState("toucan.edit.add-translation-key").CanRun);
    }
    [AvaloniaFact]
    public async Task MissingTargetsAreEditableWithoutInventingGeneratedSourceKeys()
    {
        using var host = new TestHost(); var folder = CreateCatalog(host, true); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var target = Assert.Single(vm.AllTranslation, i => i.Language == "de");
        Assert.True(target.Permissions.EditValues); Assert.Equal("title", target.Identity!.Path.Single().Property);
        var field = vm.PagingController.Data.SelectMany(g => g.Translations).Single(i => i.Language == "de"); field.Value = "Hallo {name}";
        vm.FlushPendingEdits(); await vm.SaveCommand.ExecuteAsync(null); Assert.False(vm.IsDirty);
        await vm.OpenProjectAsync(folder); Assert.Equal("Hallo {name}", vm.AllTranslation.Single(i => i.Language == "de").Value);
    }
    [AvaloniaFact]
    public async Task SourcePanelUsesFrameworkLocationsAndDisablesUnusedForDynamicExpressions()
    {
        using var host = new TestHost(); var folder = host.CreateJsonProject("scan", ("en", "{\"title\":\"Hello\",\"unused\":\"Unused\"}"));
        var settings = ProjectSettings.CreateDefault(folder); settings.Framework = "i18next"; settings.Languages = ["en"]; settings.SourceRoots = ["src", "more"]; settings.Save();
        Directory.CreateDirectory(Path.Combine(folder, "src")); Directory.CreateDirectory(Path.Combine(folder, "more"));
        File.WriteAllText(Path.Combine(folder, "src/a.ts"), "// t('comment')\nt(\n 'title', {count: 2}\n)");
        File.WriteAllText(Path.Combine(folder, "more/b.ts"), "t('missing')");
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder); await vm.ScanSourceCodeCommand.ExecuteAsync(null);
        Assert.True(vm.CanClassifyUnusedSourceKeys); Assert.Equal(2, vm.SourceCodeUsages.Count);
        var usage = Assert.Single(vm.SourceCodeUsages, u => u.Key == "title"); Assert.Equal(3, usage.Line); Assert.Equal(2, usage.Column);
        Assert.Equal(Path.Combine(folder, "src", "a.ts"), vm.ResolveSourceUsagePath(usage));
        Assert.Equal(" 'title', {count: 2}", usage.LineContent); Assert.Contains("missing", vm.UndefinedKeys);
        File.AppendAllText(Path.Combine(folder, "src/a.ts"), "\nt(dynamic)");
        await vm.ScanSourceCodeCommand.ExecuteAsync(null);
        Assert.False(vm.CanClassifyUnusedSourceKeys); Assert.NotEmpty(vm.SourceScanDiagnostics);
        vm.FilterUnusedKeysCommand.Execute(null); Assert.Null(vm.FilteredBySourceUsage);
        await vm.CloseProjectCommand.ExecuteAsync(null);
        Assert.False(host.Services.GetRequiredService<ISourceCodeService>().HasScanData);
    }
}
