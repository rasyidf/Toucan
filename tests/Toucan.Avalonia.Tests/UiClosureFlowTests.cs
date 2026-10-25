using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class UiClosureFlowTests
{
    [AvaloniaFact]
    public async Task SerializationSettingsPreviewCancelApplyUndoAndRecoveryUseNormalLifecycle()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel();
        var folder = host.CreateJsonProject("settings", ("en", "{\"a.b\":\"literal\",\"a\":{\"b\":\"nested\"},\"opaque\":null}"));
        await vm.OpenProjectAsync(folder);
        var original = File.ReadAllBytes(Path.Combine(folder, "en.json"));
        var paths = vm.AllTranslation.Select(i => i.Identity!.KeyToken).ToArray();
        var factory = host.Services.GetRequiredService<ITranslationStrategyFactory>();
        host.Dialogs.PropertiesHandler = settings =>
        {
            var properties = new ProjectPropertiesViewModel(settings, host.Dialogs) { PersistOnSave = false,
                ResourceSettings = new(settings, factory, vm.AllTranslation) };
            properties.ResourceSettings.Defaults = new() { Indent = ResourceIndent.Space4, Newline = ResourceNewline.CrLf, OutputEncoding = "UTF-8 BOM", SaveEmptyTranslations = false };
            Assert.True(properties.ResourceSettings.Preview().CanApply);
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, "en.json")));
            properties.SaveCommand.Execute(null); Assert.Empty(properties.ConfigurationError); return properties;
        };
        await vm.ShowProjectPropertiesCommand.ExecuteAsync(null);
        Assert.True(vm.IsDirty); Assert.True(host.Services.GetRequiredService<ITranslationManagementService>().IsDirty);
        Assert.Equal(paths, vm.AllTranslation.Select(i => i.Identity!.KeyToken));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, "en.json")));
        var lifecycle = (ProjectLifecycleService)host.Services.GetRequiredService<IProjectLifecycleService>(); lifecycle.FlushRecoveryDraft();
        var recovery = host.Services.GetRequiredService<IRecoveryDraftService>().TryRead(folder);
        Assert.NotNull(recovery?.Configuration); Assert.Equal(ResourceIndent.Space4, recovery.Configuration.ResourceOptions.Indent);
        vm.UndoCommand.Execute(null); Assert.Null(vm.ProjectSettings!.ResourceOptions.Indent);
        vm.RedoCommand.Execute(null); Assert.Equal(ResourceIndent.Space4, vm.ProjectSettings!.ResourceOptions.Indent);
        await vm.SaveCommand.ExecuteAsync(null); Assert.False(vm.IsDirty);
        Assert.True(File.ReadAllBytes(Path.Combine(folder, "en.json")).AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble));
        await vm.OpenProjectAsync(folder); Assert.Equal(ResourceIndent.Space4, vm.ProjectSettings!.ResourceOptions.Indent);
        Assert.Equal(paths, vm.AllTranslation.Select(i => i.Identity!.KeyToken));
    }
    [AvaloniaFact]
    public async Task GeneratedCatalogCanGainAnEditableTargetWithoutUnlockingTheSource()
    {
        using var host = new TestHost(); var folder = Directory.CreateDirectory(Path.Combine(host.Root, "generated-target")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "extracted")); File.WriteAllText(Path.Combine(folder, "extracted/en.json"), """{"a.b":{"defaultMessage":"Hello"}}""");
        var original = File.ReadAllBytes(Path.Combine(folder, "extracted/en.json"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var factory = host.Services.GetRequiredService<ITranslationStrategyFactory>();
        host.Dialogs.PropertiesHandler = settings =>
        {
            var properties = new ProjectPropertiesViewModel(settings, host.Dialogs) { PersistOnSave = false, ResourceSettings = new(settings, factory, vm.AllTranslation) };
            var row = properties.ResourceSettings.AddTarget("json"); row.Language = "fr"; row.RelativePath = "locales/fr.json";
            properties.SaveCommand.Execute(null); Assert.Empty(properties.ConfigurationError); return properties;
        };
        await vm.ShowProjectPropertiesCommand.ExecuteAsync(null);
        var target = vm.AllTranslation.Single(i => i.Language == "fr");
        Assert.True(target.Permissions.EditValues); Assert.Equal("a.b", target.Identity!.Path.Single().Property);
        var editor = vm.CreateItemViewModel(target); editor.Value = "Bonjour"; editor.Flush();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, "extracted/en.json")));
        await vm.OpenProjectAsync(folder); Assert.Equal("Bonjour", vm.AllTranslation.Single(i => i.Language == "fr").Value);
        Assert.False(vm.AllTranslation.Single(i => i.Language == "en").Permissions.EditKeys);
    }
    [AvaloniaFact]
    public async Task ArbPlaceholderOnlyEditsUndoRecoverAndSaveWithoutLosingOpaqueMetadata()
    {
        using var host = new TestHost(); var folder = Directory.CreateDirectory(Path.Combine(host.Root, "arb-ui")).FullName;
        File.WriteAllText(Path.Combine(folder, "app_en.arb"), """{"@@locale":"en","hello":"Hello {name}","@hello":{"custom":42,"placeholders":{"name":{"type":"String","custom":{"keep":true}}}}}""");
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var item = vm.CreateItemViewModel(Assert.Single(vm.AllTranslation)); var before = item.PlaceholderMetadata;
        Assert.Null(item.ApplyPlaceholderMetadata("{\"name\":{\"type\":\"String\",\"example\":\"Ada\",\"custom\":{\"keep\":true}}}", before));
        Assert.True(vm.IsDirty);
        ((ProjectLifecycleService)host.Services.GetRequiredService<IProjectLifecycleService>()).FlushRecoveryDraft();
        Assert.Single(host.Services.GetRequiredService<IRecoveryDraftService>().TryRead(folder)!.Entries);
        vm.UndoCommand.Execute(null); Assert.Equal(before, vm.AllTranslation.Single().FormatData!["arb.placeholders"]);
        vm.RedoCommand.Execute(null); await vm.SaveCommand.ExecuteAsync(null); await vm.OpenProjectAsync(folder);
        Assert.Contains("Ada", vm.AllTranslation.Single().FormatData!["arb.placeholders"]);
        Assert.Contains("\"custom\":42", File.ReadAllText(Path.Combine(folder, "app_en.arb")).Replace(" ", "").Replace("\n", "").Replace("\r", ""));
        Assert.NotNull(item.ApplyPlaceholderMetadata("[]", item.PlaceholderMetadata));
    }
    [AvaloniaFact]
    public async Task SpellingCorrectionsAreUndoableAndStaleSuggestionsCannotChangeNewText()
    {
        using var host = new TestHost(); var input = Directory.CreateDirectory(Path.Combine(host.Root, "dictionary-input")).FullName;
        File.WriteAllText(Path.Combine(input, "en.aff"), "SET UTF-8\n"); File.WriteAllText(Path.Combine(input, "en.dic"), "2\nhello\nworld\n");
        var registry = host.Services.GetRequiredService<DictionaryRegistry>(); await registry.ImportFolderAsync(input);
        var folder = host.CreateJsonProject("spell-ui", ("en", "{\"hello\":\"hello wrld\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var editor = vm.CreateItemViewModel(vm.AllTranslation.Single()); await editor.RefreshSpellingAsync();
        var issue = Assert.Single(editor.SpellingIssues); Assert.True(editor.ApplySpellingCorrection(issue, "world")); Assert.Equal("hello world", editor.Value);
        vm.UndoCommand.Execute(null); Assert.Equal("hello wrld", vm.AllTranslation.Single().Value);
        vm.RedoCommand.Execute(null); Assert.Equal("hello world", vm.AllTranslation.Single().Value);
        editor.Refresh(); await editor.RefreshSpellingAsync(); Assert.Empty(editor.SpellingIssues);
        editor.Value = "different text"; Assert.False(editor.ApplySpellingCorrection(issue, "world"));
        editor.Flush(); await vm.SaveCommand.ExecuteAsync(null); await vm.OpenProjectAsync(folder);
        Assert.Equal("different text", vm.AllTranslation.Single().Value); editor.Dispose();
    }
    [AvaloniaFact]
    public async Task InvalidAndCancelledResourceDraftsLeaveWorkspaceAndFilesUntouched()
    {
        using var host = new TestHost(); var folder = host.CreateJsonProject("invalid-layout", ("en", "{\"hello\":\"world\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var original = File.ReadAllText(Path.Combine(folder, "en.json"));
        var draft = new ResourceSettingsViewModel(ProjectConfiguration.Copy(vm.ProjectSettings!), host.Services.GetRequiredService<ITranslationStrategyFactory>(), vm.AllTranslation);
        draft.Resources.Single().RelativePath = "../outside.json";
        Assert.False(draft.Preview().CanApply); Assert.Equal(original, File.ReadAllText(Path.Combine(folder, "en.json")));
        Assert.False(vm.IsDirty);
        draft.Resources.Single().RelativePath = "copy.json";
        Assert.True(draft.Preview().CanApply); Assert.False(File.Exists(Path.Combine(folder, "copy.json")));
        Assert.Equal("en.json", vm.ProjectSettings!.ResourceDocuments.Single().Resource.RelativePath);
    }
}
