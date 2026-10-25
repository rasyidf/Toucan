using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class EditorComfortTests
{
    [AvaloniaFact]
    public void HostNotificationsReachWorkspaceHistoryAndStatusBadge()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel();
        var status = host.Services.GetRequiredService<Toucan.Avalonia.ViewModels.StatusBarViewModel>();
        var previousStatus = Toucan.Avalonia.Services.StatusBarService.Instance.ViewModel;
        Toucan.Avalonia.Services.StatusBarService.Instance.Register(status);
        var window = new Toucan.Avalonia.Views.MainWindow(vm, status); window.Show();
        try
        {
            host.Services.GetRequiredService<Toucan.Core.Plugins.INotificationCenter>().Publish("test", new Toucan.Plugins.PluginNotification { Title = "Connected" });
            Assert.Equal("Connected", Assert.Single(vm.NotificationHistory.Items).Content.Title);
            Assert.Equal(1, status.Notifications.Count);
            vm.NotificationHistory.ClearCommand.Execute(null); Assert.Equal(0, status.Notifications.Count);
        }
        finally
        {
            window.Close();
            if (previousStatus != null) Toucan.Avalonia.Services.StatusBarService.Instance.Register(previousStatus);
            else Toucan.Avalonia.Services.StatusBarService.Instance.Unregister();
        }
    }

    [AvaloniaFact]
    public async Task FocusedContextCountsUnicodeAndRespectsAuditAndGeneratedRestrictions()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("rtl-context", ("en", "{\"a\":\"Source\"}"), ("ar", "{\"a\":\"مرحبا 🌍\"}")));
        var group = Assert.Single(vm.PagingController.Data);
        Assert.True(group.AllItems.First().IsSource);
        var target = group.AllItems.Single(i => i.Language == "ar");
        vm.FocusedTranslationItem = target;
        Assert.True(target.IsRtl); Assert.Equal(7, target.CharacterCount); Assert.Equal(2, target.WordCount);
        Assert.Contains("ar", vm.FocusedTextCounts); Assert.True(vm.ApproveAndNextCommand.CanExecute(null));
        vm.EditorMode = Toucan.Avalonia.ViewModels.EditorMode.Audit;
        Assert.False(vm.ApproveAndNextCommand.CanExecute(null));
        vm.ApproveAndNextCommand.Execute(null); Assert.False(target.Model.IsApproved);
        vm.EditorMode = Toucan.Avalonia.ViewModels.EditorMode.Editor;
        target.Model.Permissions = new Toucan.Core.Models.ResourcePermissions(false, false, false, "Generated");
        Assert.False(vm.ApproveAndNextCommand.CanExecute(null));
        var original = target.Value; target.Value = "Blocked"; Assert.Equal(original, target.Value);
        var card = new Toucan.Avalonia.Views.Components.TranslationCard { DataContext = group };
        var window = new Window { Content = card, Width = 950, Height = 450 };
        window.Show();
        try
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(2, card.ComparisonColumns);
            var boxes = card.GetVisualDescendants().OfType<TextBox>().Where(b => b.Classes.Contains("translation")).ToList();
            var source = boxes.Single(b => b.DataContext is Toucan.Avalonia.ViewModels.TranslationItemViewModel { IsSource: true });
            var translated = boxes.Single(b => ReferenceEquals(b.DataContext, target));
            Assert.True(translated.TranslatePoint(default, card)!.Value.X > source.TranslatePoint(default, card)!.Value.X);
            window.Width = 420; global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(1, card.ComparisonColumns);
            Assert.True(translated.TranslatePoint(default, card)!.Value.Y > source.TranslatePoint(default, card)!.Value.Y);
        }
        finally { window.Close(); }
    }

    private sealed class ToolbarHandler : Toucan.Plugins.ICommandHandler
    {
        public int Runs;
        public bool Fail;
        public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Toucan.Plugins.CommandState GetState(Toucan.Plugins.ICommandContext context) => Toucan.Plugins.CommandState.Available;
        public Task ExecuteAsync(Toucan.Plugins.ICommandInvocation invocation, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Offline");
            Runs++; Invoked.TrySetResult(); return Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task PluginToolbarUsesWorkspaceAvailabilityAndDisappearsOnUnregister()
    {
        using var host = new TestHost(); App.RegisterSidePanels(); var vm = host.CreateViewModel();
        var previousRegistry = Toucan.Avalonia.Services.KeybindingService.Registry;
        Toucan.Avalonia.Services.KeybindingService.UseRegistry(host.Services.GetRequiredService<Toucan.Core.Commands.ICommandRegistry>());
        var window = new Toucan.Avalonia.Views.MainWindow(vm, host.Services.GetRequiredService<Toucan.Avalonia.ViewModels.StatusBarViewModel>());
        window.Show();
        var registry = Toucan.Avalonia.Services.KeybindingService.Registry;
        var handler = new ToolbarHandler();
        const string id = "test.toolbar.run";
        try
        {
            registry.Register(new Toucan.Plugins.CommandDefinition { Id = id, Title = "Toolbar action", Category = "Test", RequiresWorkspace = true, Placements = Toucan.Plugins.CommandPlacement.Toolbar }, handler, "test.toolbar");
            Assert.False(Assert.Single(vm.ContributedToolbarActions).Command.CanExecute(null));
            await vm.OpenProjectAsync(host.CreateJsonProject("toolbar", ("en", "{\"a\":\"Source\"}")));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => b.Content?.ToString() == "Toolbar action");
            Assert.True(button.IsEnabled); button.Command!.Execute(null);
            await handler.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(1, handler.Runs);
            var center = host.Services.GetRequiredService<Toucan.Core.Plugins.INotificationCenter>();
            using (var presenter = new Toucan.Avalonia.Services.NotificationPresenter(window, center, registry))
            {
                handler.Fail = true; await registry.ExecuteAsync(id);
                var failure = center.History.Last();
                Assert.Equal(Toucan.Plugins.NotificationSeverity.Error, failure.Content.Severity);
                Assert.Equal("toucan.settings.preferences", failure.Content.ActionCommandId);
            }
            registry.Unregister(id); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Empty(vm.ContributedToolbarActions);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content?.ToString() == "Toolbar action");
        }
        finally { registry.Unregister(id); window.Close(); Toucan.Avalonia.Services.KeybindingService.UseRegistry(previousRegistry); }
    }

    [Fact]
    public async Task CompletedCancelledAndFailedOperationsRemainInHistory()
    {
        var center = new Toucan.Core.Plugins.NotificationCenter();
        var operations = new Toucan.Core.Plugins.BackgroundOperationService(notifications: center);
        var options = new Toucan.Plugins.OperationOptions { Title = "Sync" };
        await operations.Start("test", options, (_, _) => Task.CompletedTask).Completion;
        await operations.Start("test", options, (_, _) => throw new OperationCanceledException()).Completion;
        await operations.Start("test", options, (_, _) => throw new InvalidOperationException("Offline")).Completion;
        Assert.Equal(["Completed", "Cancelled", "InvalidOperationException: Offline"], center.History.Select(n => n.Content.Message));
        Assert.Equal(Toucan.Plugins.NotificationSeverity.Error, center.History.Last().Content.Severity);
    }

    [AvaloniaFact]
    public async Task KeyboardSessionEditsApprovesAndSavesOneHundredTargetsAcrossPages()
    {
        using var host = new TestHost(); App.RegisterSidePanels();
        var en = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 100).ToDictionary(i => $"key{i:D3}", i => $"Source {i}"));
        var de = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 100).ToDictionary(i => $"key{i:D3}", i => $"Target {i}"));
        var vm = host.CreateViewModel();
        var window = new Toucan.Avalonia.Views.MainWindow(vm, host.Services.GetRequiredService<Toucan.Avalonia.ViewModels.StatusBarViewModel>());
        window.Show();
        try
        {
            var folder = host.CreateJsonProject("keyboard-100", ("en", en), ("de", de));
            await vm.OpenProjectAsync(folder); vm.DismissWorkflowGuideCommand.Execute(null);
            vm.FocusedTranslationItem = vm.PagingController.Data[0].AllItems.Single(i => i.Language == "de");
            var modifiers = Toucan.Avalonia.Services.PlatformService.IsMacOS ? RawInputModifiers.Meta : RawInputModifiers.Control;
            for (var i = 0; i < 100; i++)
            {
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var focused = vm.FocusedTranslationItem!;
                Assert.Equal($"key{i:D3}", focused.Namespace);
                var box = window.GetVisualDescendants().OfType<TextBox>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("translation") && ReferenceEquals(b.DataContext, focused));
                if (i == 0) box.Focus(); else Assert.True(box.IsFocused);
                box.SelectAll(); window.KeyTextInput($"Neu {i}");
                window.KeyPress(Key.Enter, modifiers, PhysicalKey.Enter, "\r");
                window.KeyRelease(Key.Enter, modifiers, PhysicalKey.Enter, "\r");
                Assert.True(focused.Model.IsApproved);
                Assert.Equal($"Neu {i}", focused.Model.Value);
            }
            Assert.Equal(100, vm.AllTranslation.Count(t => t.Language == "de" && t.IsApproved));
            Assert.True(vm.PagingController.Page > 1);
            await vm.SaveCommand.ExecuteAsync(null);
            using var reopened = new TestHost(); var again = reopened.CreateViewModel(); await again.OpenProjectAsync(folder);
            Assert.Equal(100, again.AllTranslation.Count(t => t.Language == "de" && t.IsApproved));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AcceptedPretranslationIsOneUndoStepAndStalePreviewDoesNotApply()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("preview-undo", ("en", "{\"a\":\"Hello\",\"b\":\"World\"}"), ("de", "{\"a\":\"\",\"b\":\"\"}")));
        host.Dialogs.OnPreTranslate = preview =>
        {
            foreach (var key in new[] { "a", "b" }) preview.PreviewResults.Add(new PretranslationItemResult { Namespace = key, Language = "de", Succeeded = true, TranslatedValue = "Translated " + key });
            preview.CommitCommand.Execute(null);
        };
        await vm.PreTranslateBulkCommand.ExecuteAsync(null);
        Assert.All(vm.AllTranslation.Where(t => t.Language == "de"), t => Assert.StartsWith("Translated", t.Value));
        vm.UndoCommand.Execute(null);
        Assert.All(vm.AllTranslation.Where(t => t.Language == "de"), t => Assert.Empty(t.Value));
        Assert.False(host.Services.GetRequiredService<IUndoRedoService>().CanUndo);
        host.Dialogs.OnPreTranslate = preview =>
        {
            preview.PreviewResults.Add(new PretranslationItemResult { Namespace = "a", Language = "de", Succeeded = true, TranslatedValue = "Stale" });
            vm.CreateNewItemAsync("concurrent").GetAwaiter().GetResult();
            preview.CommitCommand.Execute(null);
        };
        await vm.PreTranslateBulkCommand.ExecuteAsync(null);
        Assert.Empty(vm.AllTranslation.Single(t => t.Namespace == "a" && t.Language == "de").Value);
    }

    [AvaloniaFact]
    public void NotificationHistoryKeepsNewestFirstAndRunsOnlyRetainedActions()
    {
        var center = new Toucan.Core.Plugins.NotificationCenter(); var invoked = new List<string>();
        using var history = new Toucan.Avalonia.ViewModels.NotificationHistoryViewModel(center, id => { invoked.Add(id); return Task.CompletedTask; });
        var first = center.Publish("toucan", new Toucan.Plugins.PluginNotification { Title = "Save failed", ActionCommandId = "retry", ActionLabel = "Retry" });
        center.Publish("plugin", new Toucan.Plugins.PluginNotification { Title = "Connected" });
        Assert.Equal("Connected", history.Items[0].Content.Title);
        history.RunActionCommand.Execute(first); Assert.Equal(["retry"], invoked);
        history.ClearCommand.Execute(null); history.RunActionCommand.Execute(first);
        Assert.True(history.IsEmpty); Assert.Single(invoked);
    }

    [AvaloniaFact]
    public void OnboardingStoresLanguageAndThemeWithoutRequiringCredentials()
    {
        using var host = new TestHost();
        var theme = global::Avalonia.Application.Current!.RequestedThemeVariant;
        try
        {
            var setup = host.Services.GetRequiredService<Toucan.Avalonia.ViewModels.OnboardingViewModel>();
            setup.DefaultLanguage = "id-ID"; setup.Theme = "Dark"; setup.UseAi = false;
            setup.FinishCommand.Execute(null);
            var options = host.Services.GetRequiredService<Toucan.Core.Contracts.IPreferenceService>().Load();
            Assert.Equal("id-ID", options.DefaultLanguage); Assert.Equal("Dark", options.Theme);
            Assert.Equal(Toucan.Avalonia.ViewModels.OnboardingViewModel.CurrentVersion, options.OnboardingVersion);
            Assert.False(setup.Ai.Enabled);
        }
        finally { global::Avalonia.Application.Current.RequestedThemeVariant = theme; }
    }

    [AvaloniaFact]
    public async Task CopyBatchUndoesValuesAndApprovalTogetherAndRetainsEntryReferences()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("copy-history", ("en", "{\"a\":\"Hello\",\"b\":\"World\"}"), ("de", "{\"a\":\"Hallo\",\"b\":\"Welt\"}")));
        var targets = vm.AllTranslation.Where(t => t.Language == "de").ToList();
        foreach (var item in targets) { item.IsApproved = true; item.ApprovedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); }
        vm.SelectedKeys.Add("a"); vm.SelectedKeys.Add("b"); host.Dialogs.Picks.Enqueue("de");
        await vm.BulkCopyToLanguageCommand.ExecuteAsync(null);
        Assert.All(targets, t => Assert.False(t.IsApproved));
        Assert.Equal("Hello", targets.Single(t => t.Namespace == "a").Value);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Hallo", targets.Single(t => t.Namespace == "a").Value);
        Assert.Equal("Welt", targets.Single(t => t.Namespace == "b").Value);
        Assert.All(targets, t => Assert.True(t.IsApproved));
        Assert.False(host.Services.GetRequiredService<IUndoRedoService>().CanUndo);
        vm.RedoCommand.Execute(null);
        Assert.Equal("World", targets.Single(t => t.Namespace == "b").Value);
        Assert.All(targets, t => Assert.Contains(t, vm.AllTranslation));
    }

    [AvaloniaFact]
    public async Task ReplaceAllIsOneUndoStep()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("replace-history", ("en", "{\"a\":\"Hello one\",\"b\":\"Hello two\"}")));
        foreach (var item in vm.AllTranslation) { item.IsApproved = true; item.ApprovedAtUtc = DateTime.UtcNow; }
        vm.SearchQuery = "Hello"; vm.ReplaceText = "Goodbye";
        vm.ExecuteSearchCommand.Execute(null);
        await vm.ExecuteReplaceCommand.ExecuteAsync(null);
        Assert.All(vm.AllTranslation, t => Assert.StartsWith("Goodbye", t.Value));
        Assert.All(vm.AllTranslation, t => { Assert.False(t.IsApproved); Assert.Null(t.ApprovedAtUtc); });
        vm.UndoCommand.Execute(null);
        Assert.All(vm.AllTranslation, t => Assert.StartsWith("Hello", t.Value));
        Assert.All(vm.AllTranslation, t => { Assert.True(t.IsApproved); Assert.NotNull(t.ApprovedAtUtc); });
        Assert.False(host.Services.GetRequiredService<IUndoRedoService>().CanUndo);
    }

    [AvaloniaFact]
    public async Task MoveIsUndoableAndCollisionLeavesNoHistory()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("move-history", ("en", "{\"old.title\":\"Title\",\"taken.title\":\"Existing\"}")));
        vm.SelectedKeys.Add("old.title"); host.Dialogs.Prompts.Enqueue("taken");
        await vm.BulkMoveToNamespaceCommand.ExecuteAsync(null);
        Assert.False(host.Services.GetRequiredService<IUndoRedoService>().CanUndo);
        host.Dialogs.Prompts.Enqueue("new"); await vm.BulkMoveToNamespaceCommand.ExecuteAsync(null);
        Assert.Contains(vm.AllTranslation, t => t.Namespace == "new.title");
        vm.UndoCommand.Execute(null);
        Assert.Contains(vm.AllTranslation, t => t.Namespace == "old.title");
        Assert.Contains(vm.PagingController.Data, g => g.Namespace == "old.title");
    }

    [AvaloniaFact]
    public async Task BulkApprovalUndoesTimestampAndApprovalAsOneStep()
    {
        using var host = new TestHost(); var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("approval-history", ("en", "{\"a\":\"One\",\"b\":\"Two\"}")));
        vm.SelectedKeys.Add("a"); vm.SelectedKeys.Add("b"); vm.BulkApproveCommand.Execute(null);
        Assert.All(vm.AllTranslation, t => Assert.True(t.IsApproved));
        vm.UndoCommand.Execute(null);
        Assert.All(vm.AllTranslation, t => { Assert.False(t.IsApproved); Assert.Null(t.ApprovedAtUtc); });
        Assert.False(host.Services.GetRequiredService<IUndoRedoService>().CanUndo);
    }
}
