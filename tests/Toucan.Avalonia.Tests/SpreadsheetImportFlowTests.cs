using Avalonia.Headless.XUnit;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class SpreadsheetImportFlowTests
{
    [AvaloniaFact]
    public async Task PreviewAcceptsValuesMetadataClearsAndAdditionsAsOneUndoableChange()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("exchange", ("en", "{\"edit\":\"Original\",\"clear\":\"Clear me\",\"meta\":\"Same\"}"), ("de", "{\"edit\":\"Alt\",\"clear\":\"Bleibt\",\"meta\":\"Gleich\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var before = vm.AllTranslation.Select(i => ResourceEntryOperations.Copy(i)).ToList();
        var path = Path.Combine(host.Root, "review.xlsx");
        var edits = vm.AllTranslation.Where(i => i.Language == "en").Select(i => ResourceEntryOperations.Copy(i)).ToList();
        edits.Single(i => i.Namespace == "edit").Value = "Changed";
        edits.Single(i => i.Namespace == "clear").Value = "";
        var meta = edits.Single(i => i.Namespace == "meta"); meta.Comment = "Reviewed"; meta.IsApproved = true;
        edits.Add(new TranslationItem { Namespace = "new", Language = "en", Value = "Added" });
        ExcelService.Write(path, edits, ExcelService.Toucan);
        host.Dialogs.OnSpreadsheetPreview = preview =>
        {
            Assert.Equal(1, preview.Additions); Assert.Equal(1, preview.Edits); Assert.Equal(1, preview.Clears); Assert.Equal(1, preview.MetadataOnly);
            Assert.Empty(preview.Conflicts);
            Assert.Equal("Original", vm.AllTranslation.Single(i => i.Namespace == "edit" && i.Language == "en").Value);
            Assert.False(vm.IsDirty);
            return true;
        };
        host.Dialogs.Files.Enqueue(path); await vm.ImportExcelCommand.ExecuteAsync(null);
        Assert.True(vm.IsDirty);
        Assert.Equal(7, vm.AllTranslation.Count);
        Assert.Equal("Changed", vm.AllTranslation.Single(i => i.Namespace == "edit" && i.Language == "en").Value);
        Assert.Equal("", vm.AllTranslation.Single(i => i.Namespace == "clear" && i.Language == "en").Value);
        Assert.Equal("Alt", vm.AllTranslation.Single(i => i.Namespace == "edit" && i.Language == "de").Value);
        Assert.Contains("4 change(s)", vm.StatusText);
        ((ProjectLifecycleService)host.Services.GetRequiredService<IProjectLifecycleService>()).FlushRecoveryDraft();
        var draft = host.Services.GetRequiredService<IRecoveryDraftService>().TryRead(folder);
        Assert.NotNull(draft);
        Assert.Contains(draft.Entries, e => e.Namespace == "new" && e.Value == "Added" && e.Identity != null);
        Assert.Contains(draft.Entries, e => e.Namespace == "meta" && e.Comment == "Reviewed");
        Assert.Contains(draft.Entries, e => e.Namespace == "edit" && e.BaseValue == "Original" && e.Value == "Changed");
        Assert.Equal(4, draft.Entries.Count); // untouched German entries keep their saved baselines
        vm.UndoCommand.Execute(null);
        Assert.Equal(before.Select(State).Order(), vm.AllTranslation.Select(State).Order());
        vm.RedoCommand.Execute(null);
        Assert.Equal(7, vm.AllTranslation.Count);
        Assert.True(vm.AllTranslation.Single(i => i.Namespace == "meta" && i.Language == "en").IsApproved);
        await vm.SaveCommand.ExecuteAsync(null); Assert.False(vm.IsDirty);
        await vm.OpenProjectAsync(folder);
        Assert.Equal("Added", vm.AllTranslation.Single(i => i.Namespace == "new" && i.Language == "en").Value);
        var restored = vm.AllTranslation.Single(i => i.Namespace == "meta" && i.Language == "en");
        Assert.Equal("Reviewed", restored.Comment); Assert.True(restored.IsApproved);
    }

    [AvaloniaFact]
    public async Task CancelAndConflictLeaveWorkspaceHistoryAndDiskUntouched()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("cancel", ("en", "{\"title\":\"Original\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var content = File.ReadAllText(Path.Combine(folder, "en.json"));
        var original = State(Assert.Single(vm.AllTranslation));
        var path = Path.Combine(host.Root, "cancel.xlsx");
        ExcelService.Write(path, [new TranslationItem { Namespace = "title", Language = "en", Value = "Changed" }], ExcelService.Toucan);
        host.Dialogs.OnSpreadsheetPreview = _ => false;
        host.Dialogs.Files.Enqueue(path); await vm.ImportExcelCommand.ExecuteAsync(null);
        Assert.Equal(original, State(Assert.Single(vm.AllTranslation))); Assert.False(vm.IsDirty);
        Assert.False(host.Services.GetRequiredService<IUndoRedoService>().CanUndo);
        ExcelService.Write(path, [new TranslationItem { Namespace = "title", Language = "fr", Value = "Unknown" }], ExcelService.Toucan);
        host.Dialogs.OnSpreadsheetPreview = _ => true; // even a misbehaving dialog cannot apply conflicts
        host.Dialogs.Files.Enqueue(path); await vm.ImportExcelCommand.ExecuteAsync(null);
        Assert.Single(host.Dialogs.LastSpreadsheetPreview!.Conflicts);
        Assert.Equal(original, State(Assert.Single(vm.AllTranslation))); Assert.False(vm.IsDirty);
        Assert.Equal(content, File.ReadAllText(Path.Combine(folder, "en.json")));
    }

    [AvaloniaFact]
    public async Task StalePreviewCannotOverwriteConcurrentWorkspaceEdits()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("stale", ("en", "{\"title\":\"Original\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var path = Path.Combine(host.Root, "stale.xlsx");
        ExcelService.Write(path, [new TranslationItem { Namespace = "title", Language = "en", Value = "Imported" }], ExcelService.Toucan);
        host.Dialogs.OnSpreadsheetPreview = _ =>
        {
            vm.PagingController.Data.Single().Translations.Single().Value = "Concurrent";
            vm.FlushPendingEdits(); return true;
        };
        host.Dialogs.Files.Enqueue(path); await vm.ImportExcelCommand.ExecuteAsync(null);
        Assert.Equal("Concurrent", Assert.Single(vm.AllTranslation).Value);
        Assert.Contains(host.Messages.Shown, m => m.Contains("workspace changed", StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task StrictApprovalValidatesProposedValuesBeforeApplyingAnything()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("strict-exchange", ("en", "{\"title\":\"Hello {name}\"}"), ("de", "{\"title\":\"Hallo {name}\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.RequireValidForApproval = true;
        var path = Path.Combine(host.Root, "strict.xlsx");
        ExcelService.Write(path, [new TranslationItem { Namespace = "title", Language = "de", Value = "Hallo", IsApproved = true }], ExcelService.Toucan);
        host.Dialogs.Files.Enqueue(path); await vm.ImportExcelCommand.ExecuteAsync(null);
        Assert.NotEmpty(host.Dialogs.LastSpreadsheetPreview!.Conflicts);
        Assert.Equal("Hallo {name}", vm.AllTranslation.Single(i => i.Language == "de").Value);
        Assert.False(vm.IsDirty);
    }

    [AvaloniaFact]
    public async Task CompatibilityDialectIsRegisteredAlongsideNativeExport()
    {
        using var host = new TestHost();
        Assert.Contains(host.Services.GetServices<SpreadsheetDialect>(), d => d == BabelSpreadsheetDialect.Instance);
        var folder = host.CreateJsonProject("dialects", ("en", "{\"title\":\"Hello\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        foreach (var dialect in new[] { ExcelService.Toucan, BabelSpreadsheetDialect.Instance })
        {
            var path = Path.Combine(host.Root, dialect.Id + ".xlsx");
            host.Dialogs.Picks.Enqueue(dialect.Label); host.Dialogs.SaveFiles.Enqueue(path);
            await vm.ExportExcelCommand.ExecuteAsync(null);
            using var book = new XLWorkbook(path);
            Assert.Equal(dialect.KeyHeader, book.Worksheet(1).Cell(1, 2).GetString());
            Assert.Empty(SpreadsheetPlanner.Plan(ExcelService.Read(path), vm.AllTranslation).Changes);
        }
        Assert.False(vm.IsDirty);
    }

    [AvaloniaFact]
    public async Task AcceptedWorkbookIsRecoveredAfterRestartWithOriginalIdentityAndMetadata()
    {
        using var first = new TestHost();
        var folder = first.CreateJsonProject("recover-exchange", ("en", "{\"title\":\"Original\"}"));
        var vm = first.CreateViewModel(); await vm.OpenProjectAsync(folder);
        var item = ResourceEntryOperations.Copy(Assert.Single(vm.AllTranslation));
        var entryId = item.Identity!.EntryId;
        item.Value = "Recovered"; item.Comment = "Reviewed"; item.IsApproved = true;
        var path = Path.Combine(first.Root, "recovery.xlsx");
        ExcelService.Write(path, [item, new TranslationItem { Namespace = "added", Language = "en", Value = "Added" }], ExcelService.Toucan);
        first.Dialogs.Files.Enqueue(path); await vm.ImportExcelCommand.ExecuteAsync(null);
        ((ProjectLifecycleService)first.Services.GetRequiredService<IProjectLifecycleService>()).FlushRecoveryDraft();
        using var second = new TestHost(first.Root);
        var restored = second.CreateViewModel(); await restored.OpenProjectAsync(folder);
        var recovered = restored.AllTranslation.Single(i => i.Namespace == "title");
        Assert.Equal("Recovered", recovered.Value); Assert.Equal("Reviewed", recovered.Comment); Assert.True(recovered.IsApproved);
        Assert.Equal(entryId, recovered.Identity!.EntryId);
        Assert.Equal("Added", restored.AllTranslation.Single(i => i.Namespace == "added").Value);
        Assert.True(restored.IsDirty);
        Assert.Contains("Original", File.ReadAllText(Path.Combine(folder, "en.json")));
    }

    [AvaloniaFact]
    public async Task PreviewDialogExposesCountsDetailsAndDisablesApplyForConflicts()
    {
        using var host = new TestHost();
        var preview = new SpreadsheetPreview([], [], ["Unknown package"]);
        var dialog = new Toucan.Avalonia.Views.Dialogs.SpreadsheetPreviewDialog(preview);
        var panel = Assert.IsType<global::Avalonia.Controls.DockPanel>(dialog.Content);
        var footer = Assert.IsType<global::Avalonia.Controls.StackPanel>(panel.Children[0]);
        Assert.False(Assert.IsType<global::Avalonia.Controls.Button>(footer.Children[1]).IsEnabled);
        Assert.Contains("1 conflicts", Assert.IsType<global::Avalonia.Controls.TextBlock>(panel.Children[1]).Text);
        Assert.Contains("Unknown package", Assert.IsType<global::Avalonia.Controls.ListBox>(panel.Children[2]).ItemsSource!.Cast<string>().Single());
        var detailed = new SpreadsheetPreview([], [new SpreadsheetChange(2, "main", "title", "title", "en", "Metadata only", "Hello", "Hello", "Old note", "Reviewed", false, true)], []);
        var detailsPanel = Assert.IsType<global::Avalonia.Controls.DockPanel>(new Toucan.Avalonia.Views.Dialogs.SpreadsheetPreviewDialog(detailed).Content);
        var detail = Assert.IsType<global::Avalonia.Controls.ListBox>(detailsPanel.Children[2]).ItemsSource!.Cast<string>().Single();
        Assert.Contains("Old note", detail); Assert.Contains("Reviewed", detail); Assert.Contains("False", detail); Assert.Contains("True", detail);
        await Task.CompletedTask;
    }

    private static string State(TranslationItem i) => $"{i.Identity?.EntryId}|{i.Identity?.Address}|{i.Namespace}|{i.Language}|{i.Value}|{i.Comment}|{i.IsApproved}";
}
