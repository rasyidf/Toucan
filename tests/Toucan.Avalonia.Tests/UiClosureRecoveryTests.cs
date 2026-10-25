using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Controls.Presenters;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Toucan.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class UiClosureRecoveryTests
{
    private static string Migration(TestHost host)
    {
        var root = Directory.CreateDirectory(Path.Combine(host.Root, "migration-preview")).FullName;
        File.WriteAllText(Path.Combine(root, "one.json"), "{\"one\":\"First\"}"); File.WriteAllText(Path.Combine(root, "two.json"), "{\"two\":\"Second\"}");
        foreach (var name in new[] { "one", "two" }) File.WriteAllText(Path.Combine(root, name + ".babel"), $"<babeledit_project version=\"1.3\"><framework>generic-json</framework><translation_packages><translation_package><name>main</name><translation_urls><translation_url><path>{name}.json</path><language>en</language></translation_url></translation_urls></translation_package></translation_packages></babeledit_project>");
        return root;
    }
    [AvaloniaFact]
    public async Task ExactMigrationSelectionPreviewCancelAndChangedInputsPreserveActiveProject()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel(); var active = host.CreateJsonProject("active", ("en", "{\"keep\":\"active\"}"));
        await vm.OpenProjectAsync(active); var folder = Migration(host); var chosen = Path.Combine(folder, "two.babel");
        host.Dialogs.AcceptMigration = false; host.Dialogs.Files.Enqueue(chosen); await vm.OpenProjectFileCommand.ExecuteAsync(null);
        Assert.Equal(active, vm.CurrentPath); Assert.False(File.Exists(Path.Combine(folder, "toucan.tproj")));
        host.Dialogs.OnMigrationReport = _ => { File.AppendAllText(Path.Combine(folder, "two.json"), " "); return true; };
        await vm.OpenProjectAsync(chosen); Assert.Equal(active, vm.CurrentPath);
        host.Dialogs.OnMigrationReport = null; host.Dialogs.AcceptMigration = true;
        await vm.OpenProjectAsync(chosen); Assert.Equal("Second", vm.AllTranslation.Single().Value);
        Assert.Equal("two.babel", vm.ProjectSettings!.LastMigrationReport!.Source);
        var before = File.ReadAllBytes(chosen); await vm.SaveCommand.ExecuteAsync(null); Assert.Equal(before, File.ReadAllBytes(chosen));
        Assert.NotNull(ProjectSettings.LoadFrom(folder)!.LastMigrationReport);
    }
    [AvaloniaFact]
    public async Task SameFolderMigrationCannotSaveOverThePreviewOrCloseUnsavedEdits()
    {
        using var host = new TestHost(); var root = host.CreateJsonProject("same-folder", ("en", "{\"hello\":\"original\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        vm.PagingController.Data.Single().Translations.Single().Value = "unsaved"; vm.FlushPendingEdits();
        var file = Path.Combine(root, "same.babel");
        File.WriteAllText(file, "<babeledit_project version=\"1.3\"><framework>generic-json</framework><translation_packages><translation_package><name>main</name><translation_urls><translation_url><path>en.json</path><language>en</language></translation_url></translation_urls></translation_package></translation_packages></babeledit_project>");
        await vm.OpenProjectAsync(file);
        Assert.Equal("unsaved", vm.AllTranslation.Single().Value); Assert.Equal(root, vm.CurrentPath);
        Assert.Contains("original", File.ReadAllText(Path.Combine(root, "en.json")));
        Assert.True(vm.IsDirty); Assert.Contains(host.Messages.Shown, m => m.Contains("Save the active", StringComparison.Ordinal));
    }
    [AvaloniaFact]
    public async Task SettingsOnlyRecoveryAfterRestartRestoresConfigurationWithoutChangingValues()
    {
        using var first = new TestHost(); var folder = first.CreateJsonProject("recover-config", ("en", "{\"hello\":\"world\"}"));
        var vm = first.CreateViewModel(); await vm.OpenProjectAsync(folder);
        Assert.Equal(ProjectSaveStatus.Success, (await first.Services.GetRequiredService<IProjectLifecycleService>().SaveProjectAsync()).Status);
        var original = File.ReadAllBytes(Path.Combine(folder, "en.json"));
        first.Dialogs.PropertiesHandler = draft =>
        {
            var properties = new ProjectPropertiesViewModel(draft, first.Dialogs) { PersistOnSave = false };
            draft.Spelling.Fallbacks["en-us"] = "en"; properties.SaveCommand.Execute(null); return properties;
        };
        await vm.ShowProjectPropertiesCommand.ExecuteAsync(null);
        ((ProjectLifecycleService)first.Services.GetRequiredService<IProjectLifecycleService>()).FlushRecoveryDraft();
        var draft = first.Services.GetRequiredService<IRecoveryDraftService>().TryRead(folder)!;
        // Model a restart with the same project path and a separate service graph; prevent the first timer replacing the retained draft.
        using var second = new TestHost(); var recovery = second.Services.GetRequiredService<IRecoveryDraftService>(); recovery.Write(folder, draft);
        var lifecycle = second.Services.GetRequiredService<IProjectLifecycleService>(); var opened = await lifecycle.OpenProjectAsync(folder);
        Assert.NotNull(opened.RecoveryDraft); var applied = lifecycle.ApplyRecovery(); Assert.Empty(applied.Conflicts); Assert.True(applied.Applied > 0);
        Assert.Equal("en", lifecycle.CurrentProject!.Spelling.Fallbacks["en-us"]);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, "en.json")));
        Assert.True(second.Services.GetRequiredService<ITranslationManagementService>().IsDirty);
    }
    [AvaloniaFact]
    public async Task ProjectWordListUndoAndMissingDictionariesDoNotBlockSave()
    {
        using var host = new TestHost(); var input = Directory.CreateDirectory(Path.Combine(host.Root, "words")).FullName;
        File.WriteAllText(Path.Combine(input, "en.aff"), "SET UTF-8\n"); File.WriteAllText(Path.Combine(input, "en.dic"), "1\nhello\n");
        var registry = host.Services.GetRequiredService<DictionaryRegistry>(); var pack = Assert.Single(await registry.ImportFolderAsync(input));
        var folder = host.CreateJsonProject("words-project", ("en", "{\"hello\":\"Toucan\"}")); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
        using var editor = vm.CreateItemViewModel(vm.AllTranslation.Single()); await editor.RefreshSpellingAsync(); var issue = Assert.Single(editor.SpellingIssues);
        editor.RememberSpelling(issue, true); Assert.Contains("Toucan", vm.ProjectSettings!.Spelling.Words["en"]);
        vm.UndoCommand.Execute(null); Assert.False(vm.ProjectSettings.Spelling.Words.ContainsKey("en"));
        vm.RedoCommand.Execute(null); registry.Remove(pack.Id); await editor.RefreshSpellingAsync(); Assert.Empty(editor.SpellingIssues);
        await vm.CheckProjectSpellingCommand.ExecuteAsync(null); Assert.Contains(vm.ValidationIssues, i => i.RuleId == "local-spelling-availability");
        await vm.SaveCommand.ExecuteAsync(null); Assert.False(vm.IsDirty);
        var copy = Path.Combine(host.Root, "words-copy"); Assert.Equal(ProjectSaveStatus.Success, (await host.Services.GetRequiredService<IProjectLifecycleService>().SaveProjectAsAsync(copy)).Status);
        Assert.Contains("Toucan", ProjectSettings.LoadFrom(copy)!.Spelling.Words["en"]);
    }
    [AvaloniaFact]
    public async Task StructuredPlaceholderControlsApplyOneUndoableMetadataEdit()
    {
        using var host = new TestHost(); var root = Directory.CreateDirectory(Path.Combine(host.Root, "metadata-controls")).FullName;
        File.WriteAllText(Path.Combine(root, "app_en.arb"), """{"hello":"Hello {name}","@hello":{"placeholders":{"name":{"type":"String","custom":{"preserve":true}}}}}""");
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(root);
        using var row = vm.CreateItemViewModel(vm.AllTranslation.Single());
        var control = new ArbPlaceholderEditor { DataContext = row }; var window = new Window { Width = 500, Height = 500, Content = control };
        window.Show();
        try
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            control.GetVisualDescendants().OfType<TextBox>().Single(b => b.PlaceholderText == "Example").Text = "Ada";
            control.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply placeholders")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("Ada", vm.AllTranslation.Single().FormatData!["arb.placeholders"]);
            Assert.Contains("preserve", vm.AllTranslation.Single().FormatData!["arb.placeholders"]);
            vm.UndoCommand.Execute(null); Assert.DoesNotContain("Ada", vm.AllTranslation.Single().FormatData!["arb.placeholders"]);
        }
        finally { window.Close(); }
    }
    [Fact]
    public void SourceLocationArgumentsAreSubstitutedAfterTemplateParsing()
    {
        var file = "source file\";touch unwanted;\".vue";
        var start = PlatformService.EditorStartInfo("\"my editor.exe\" --goto \"{file}:{line}:{column}\"", file, 12, 4);
        Assert.False(start.UseShellExecute); Assert.Equal("my editor.exe", start.FileName);
        Assert.Equal(["--goto", file + ":12:4"], start.ArgumentList);
        Assert.Throws<FormatException>(() => PlatformService.EditorStartInfo("editor \"unclosed", "file", 1, 1));
    }
    [AvaloniaFact]
    public async Task RightClickAtAMisspellingOffersCorrectionsEvenWhenTheCaretIsElsewhere()
    {
        using var host = new TestHost(); App.RegisterSidePanels();
        var input = Directory.CreateDirectory(Path.Combine(host.Root, "menu-dictionary")).FullName;
        File.WriteAllText(Path.Combine(input, "en.aff"), "SET UTF-8\n"); File.WriteAllText(Path.Combine(input, "en.dic"), "2\nhello\nworld\n");
        await host.Services.GetRequiredService<DictionaryRegistry>().ImportFolderAsync(input);
        var root = host.CreateJsonProject("spelling-menu", ("en", "{\"hello\":\"hello wrld\"}"));
        var vm = host.CreateViewModel(); var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await vm.OpenProjectAsync(root); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var box = window.GetVisualDescendants().OfType<TextBox>().First(b => b.Classes.Contains("translation"));
            var row = Assert.IsType<TranslationItemViewModel>(box.DataContext); await row.RefreshSpellingAsync(); Assert.Single(row.SpellingIssues);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var output = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
            if (output != null) { Directory.CreateDirectory(output); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(output, "editor-spelling.png")); }
            box.CaretIndex = 0;
            var presenter = box.GetVisualDescendants().OfType<TextPresenter>().First();
            var rectangle = presenter.TextLayout.HitTestTextPosition(7);
            var point = Assert.NotNull(presenter.TranslatePoint(new Point(rectangle.X + 2, rectangle.Y + rectangle.Height / 2), window));
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var menu = Assert.IsType<ContextMenu>(box.FindAncestorOfType<TranslationCard>()!.LastSpellingMenu);
            var correction = menu.ItemsSource!.Cast<MenuItem>().Single(i => Equals(i.Header, "world"));
            correction.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Close();
            Assert.Equal("hello world", vm.AllTranslation.Single().Value); vm.UndoCommand.Execute(null); Assert.Equal("hello wrld", vm.AllTranslation.Single().Value);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task ACorrectionFromAnOldProjectCannotAddHistoryOrEditTheNewWorkspace()
    {
        using var host = new TestHost(); var input = Directory.CreateDirectory(Path.Combine(host.Root, "stale-pack")).FullName;
        File.WriteAllText(Path.Combine(input, "en.aff"), "SET UTF-8\n"); File.WriteAllText(Path.Combine(input, "en.dic"), "1\nworld\n");
        await host.Services.GetRequiredService<DictionaryRegistry>().ImportFolderAsync(input);
        var old = host.CreateJsonProject("old-spelling", ("en", "{\"title\":\"wrld\"}")); var current = host.CreateJsonProject("current-spelling", ("en", "{\"title\":\"Different\"}"));
        var vm = host.CreateViewModel(); await vm.OpenProjectAsync(old);
        using var editor = vm.CreateItemViewModel(vm.AllTranslation.Single()); await editor.RefreshSpellingAsync(); var issue = Assert.Single(editor.SpellingIssues);
        await vm.OpenProjectAsync(current);
        Assert.False(editor.ApplySpellingCorrection(issue, "world")); Assert.False(host.Services.GetRequiredService<IUndoRedoService>().CanUndo);
        Assert.Equal("Different", vm.AllTranslation.Single().Value); Assert.False(vm.IsDirty);
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResourceSpellingAndMetadataDialogsRenderAtNormalSize(bool dark)
    {
        using var host = new TestHost(); var app = Application.Current!; var previous = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        try
        {
            var folder = host.CreateJsonProject("render-resources", ("en", "{\"hello\":\"world\"}")); var vm = host.CreateViewModel(); await vm.OpenProjectAsync(folder);
            var draft = ProjectConfiguration.Copy(vm.ProjectSettings!);
            var properties = new ProjectPropertiesViewModel(draft, host.Dialogs) { PersistOnSave = false,
                ResourceSettings = new(draft, host.Services.GetRequiredService<ITranslationStrategyFactory>(), vm.AllTranslation) };
            var dialog = new ProjectPropertiesDialog(properties, host.Services.GetRequiredService<DictionaryRegistry>(), host.Dialogs);
            dialog.Show();
            try
            {
                foreach (var page in new[] { 5, 6 })
                {
                    dialog.FindControl<ListBox>("Nav")!.SelectedIndex = page; global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = dialog.CaptureRenderedFrame(); Assert.NotNull(frame);
                    var output = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
                    if (output != null) { var path = Path.Combine(output, dark ? "dark" : "light"); Directory.CreateDirectory(path); frame.Save(Path.Combine(path, page == 5 ? "resource-settings.png" : "spelling-settings.png")); }
                }
            }
            finally { dialog.Close(); }
        }
        finally { app.RequestedThemeVariant = previous; }
    }
}
