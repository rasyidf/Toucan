using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class ResourceIdentityFlowTests
{
    [AvaloniaFact]
    public async Task MixedWorkspaceValueAndRenameUndoAsOneIdentityPreservingStep()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("mixed-workspace", ("en", """{"title":"Original"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        var identity = vm.AllTranslation.Single().Identity!;
        var result = vm.ApplyWorkspaceEdit("test", new Toucan.Plugins.WorkspaceEdit().SetValue("title", "en", "Edited").RenameKey("title", "heading"));
        Assert.True(result.Applied);
        Assert.Equal("heading", vm.AllTranslation.Single().Namespace);
        vm.UndoCommand.Execute(null);
        Assert.Equal("title", vm.AllTranslation.Single().Namespace);
        Assert.Equal("Original", vm.AllTranslation.Single().Value);
        Assert.Equal(identity.EntryId, vm.AllTranslation.Single().Identity!.EntryId);
        Assert.False(host.Services.GetRequiredService<Toucan.Core.Contracts.Services.IUndoRedoService>().CanUndo);
    }
    [AvaloniaFact]
    public async Task AddedLanguageKeepsLiteralAndNestedKeysThroughSaveAndReopen()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("language-identity", ("en", """{"a.b":"Literal","a":{"b":"Nested"}}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        await vm.AddLanguageAsync("de");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        await vm.OpenProjectAsync(folder);
        Assert.Equal(2, vm.AllTranslation.Count(i => i.Language == "de"));
        Assert.Equal(2, vm.AllTranslation.Where(i => i.Language == "de").Select(i => i.Identity!.KeyToken).Distinct().Count());
    }

    [AvaloniaFact]
    public async Task CollisionRenameDuplicateDeleteUndoAndSavePreserveTypedIdentity()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("identity", ("en", """{"a.b":"Literal","a":{"b":"Nested"},"items":["Array"],"items[0]":"Literal index"}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        var literal = vm.AllTranslation.Single(i => i.Value == "Literal");
        literal.Comment = "Keep metadata";
        literal.IsApproved = true;
        var selector = literal.Namespace;
        var identity = literal.Identity!;
        Assert.Equal(4, vm.AllTranslation.Select(i => i.Namespace).Distinct().Count());

        Assert.Null(vm.RenameItemCore(new NsTreeItem { Namespace = selector, Name = "a.b" }, "renamed"));
        Assert.Equal("renamed", vm.AllTranslation.Single(i => i.Value == "Literal").Identity!.Path.Single().Property);
        vm.UndoCommand.Execute(null);
        var restored = vm.AllTranslation.Single(i => i.Value == "Literal");
        Assert.Equal(identity.KeyToken, restored.Identity!.KeyToken);
        Assert.Equal(identity.EntryId, restored.Identity.EntryId);
        Assert.Equal(selector, restored.Namespace);
        vm.RedoCommand.Execute(null);
        Assert.Equal("renamed", vm.AllTranslation.Single(i => i.Value == "Literal").Namespace);
        vm.UndoCommand.Execute(null);

        vm.SelectedNode = new NsTreeItem { Namespace = selector, Name = "a.b" };
        vm.DuplicateItemCommand.Execute(null);
        var copy = vm.AllTranslation.Single(i => i.Namespace == selector + "_copy");
        Assert.NotEqual(identity.EntryId, copy.Identity!.EntryId);
        Assert.Equal("Keep metadata", copy.Comment);
        vm.UndoCommand.Execute(null);
        Assert.Equal(4, vm.AllTranslation.Count);
        vm.DeleteNamespaces([selector]);
        Assert.Equal(3, vm.AllTranslation.Count);
        vm.UndoCommand.Execute(null);
        Assert.Equal(4, vm.AllTranslation.Count);

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        await vm.OpenProjectAsync(folder);
        restored = vm.AllTranslation.Single(i => i.Value == "Literal");
        Assert.Equal(identity.EntryId, restored.Identity!.EntryId);
        Assert.Equal("Keep metadata", restored.Comment);
        Assert.True(restored.IsApproved);
        Assert.Equal(4, vm.AllTranslation.Select(i => i.Namespace).Distinct().Count());
    }
}
