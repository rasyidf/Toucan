using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Avalonia.Views.Components;
using Toucan.Core.Contracts;
using Toucan.Core.Options;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class InlineEditingTests
{
    private static string Project(TestHost host) => host.CreateJsonProject("inline",
        ("en", """{"greeting":"Hello {{name}}, {count} items cost %s. Reference {0}."}"""),
        ("fr", """{"greeting":"Bonjour {{name}}, {count} articles coûtent %s. Référence {0}."}"""));

    [AvaloniaFact]
    public async Task InlineValidationDebouncesAndUsesLiveSourceAndTargetWithoutSaving()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(Project(host));
        var group = vm.PagingController.Data.Single();
        var en = group.Translations.Single(t => t.Language == "en");
        var fr = group.Translations.Single(t => t.Language == "fr");
        Assert.False(fr.HasInlineErrors);
        var original = fr.Model.Value;
        fr.Value = "Bonjour {{other}}, {count} articles coûtent %s. Référence {0}.";
        Assert.Equal(original, fr.Model.Value);
        Assert.False(fr.HasInlineErrors);
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        Assert.True(fr.HasInlineErrors);
        var issue = Assert.Single(fr.InlineIssues, i => i.RuleId == "placeholder-mismatch");
        Assert.Contains("{{name}}", issue.Message);
        Assert.Contains("{{other}}", issue.Message);

        fr.Value = "Bonjour {{name}}, {count} articles coûtent %s. Référence {0}.";
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        Assert.False(fr.HasInlineErrors);
        en.Value = "Hello {{name}} {{new}}, {count} items cost %s. Reference {0}.";
        Assert.Equal(en.Value, fr.SourceText);
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(fr.InlineIssues, i => i.Message.Contains("{{new}}", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task InlineValidationUsesAppDefaultsUnlessProjectOverridesThem()
    {
        using var host = new TestHost();
        var defaultsService = host.Services.GetRequiredService<IProjectDefaultsService>();
        var defaults = defaultsService.Load();
        defaults.ValidationRules["placeholder-mismatch"] = new(false, "Error");
        defaultsService.Save(defaults);
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(Project(host));
        var fr = vm.PagingController.Data.Single().Translations.Single(t => t.Language == "fr");
        fr.Value = "Bonjour {{wrong}}";
        fr.RefreshInlineValidation();
        Assert.DoesNotContain(fr.InlineIssues, i => i.RuleId == "placeholder-mismatch");
        vm.ProjectSettings!.ValidationRules = new() { ["placeholder-mismatch"] = new(true, "Error") };
        fr.RefreshInlineValidation();
        Assert.True(fr.HasInlineErrors);
        PanelService.Instance.ShowPlaceholderWarnings = false;
        fr.RefreshPlaceholderWarning();
        Assert.DoesNotContain(fr.InlineIssues, i => i.RuleId == "placeholder-mismatch");
    }

    [AvaloniaFact]
    public async Task InlineValidationHonorsProjectOverridesAndCanBeClearedByUndo()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(Project(host));
        var fr = vm.PagingController.Data.Single().Translations.Single(t => t.Language == "fr");
        fr.Value = "Bonjour {{wrong}}";
        fr.Flush();
        fr.RefreshInlineValidation();
        Assert.True(fr.HasInlineErrors);
        vm.ProjectSettings!.ValidationRules = new() { ["placeholder-mismatch"] = new ValidationRuleConfig(true, "Warning") };
        fr.RefreshInlineValidation();
        Assert.False(fr.HasInlineErrors);
        Assert.Equal(ValidationSeverity.Warning, Assert.Single(fr.InlineIssues, i => i.RuleId == "placeholder-mismatch").Severity);
        vm.ProjectSettings.ValidationRules["placeholder-mismatch"] = new(false, "Error");
        fr.RefreshInlineValidation();
        Assert.DoesNotContain(fr.InlineIssues, i => i.RuleId == "placeholder-mismatch");
        vm.ProjectSettings.ValidationRules = null;
        vm.UndoCommand.Execute(null);
        fr.RefreshInlineValidation();
        Assert.False(fr.HasInlineErrors);
        fr.Value = "";
        fr.RefreshInlineValidation();
        Assert.Contains(fr.InlineIssues, i => i.RuleId == "missing-translation");
        Assert.DoesNotContain(fr.InlineIssues, i => i.RuleId == "empty-value");
    }

    [AvaloniaTheory]
    [InlineData(EditorLayout.Cards, false, 880)]
    [InlineData(EditorLayout.Rows, true, 1280)]
    [InlineData(EditorLayout.Focused, false, 880)]
    public async Task HighlightAndValidationRenderWithoutInterceptingTyping(EditorLayout layout, bool dark, int width)
    {
        using var host = new TestHost();
        App.RegisterSidePanels();
        var oldTheme = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var vm = host.CreateViewModel();
        var window = new MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>()) { Width = width, Height = 800 };
        try
        {
            window.Show();
            await vm.OpenProjectAsync(Project(host));
            vm.SetEditorLayoutCommand.Execute(layout);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var card = Assert.Single(window.GetVisualDescendants().OfType<TranslationCard>(), c => c.IsEffectivelyVisible);
            var box = card.GetVisualDescendants().OfType<TextBox>().Single(b => b.Classes.Contains("translation") &&
                b.DataContext is TranslationItemViewModel { Language: "fr" });
            Assert.True(box.Focus());
            box.SelectAll();
            window.KeyTextInput("Bonjour {{other}}, {count} articles coûtent %s. Référence {0}.");
            var row = Assert.IsType<TranslationItemViewModel>(box.DataContext);
            row.RefreshInlineValidation();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(box.Text, row.Value);
            Assert.True(box.IsFocused);
            Assert.Contains("invalid", box.Classes);
            var highlight = Assert.Single(card.GetVisualDescendants().OfType<PlaceholderHighlight>(), h => ReferenceEquals(h.Editor, box));
            Assert.False(highlight.IsHitTestVisible);
            Assert.NotNull(highlight.HighlightBrush);
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("Unexpected:", StringComparison.Ordinal) == true);
            var directory = Environment.GetEnvironmentVariable("TOUCAN_TEST_SCREENSHOTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"inline-{layout}-{width}.png"));
            }
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = oldTheme;
        }
    }
}
