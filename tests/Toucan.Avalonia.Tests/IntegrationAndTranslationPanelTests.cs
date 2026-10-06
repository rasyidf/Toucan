using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Avalonia.Views.Panels;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>File association registration (Linux files, against a temp data home) and the Machine Translation panel.</summary>
public sealed class IntegrationAndTranslationPanelTests : IDisposable
{
    private const string Exe = "/opt/Toucan App/Toucan";

    private readonly string _dataHome = Directory.CreateTempSubdirectory("toucan-xdg-").FullName;

    public void Dispose() => Directory.Delete(_dataHome, recursive: true);

    // --- Linux file association --------------------------------------------------------------------------------

    [Fact]
    public void LinuxInstall_WritesMimeTypeAndDesktopEntry_AndRefreshesCaches()
    {
        var calls = new List<string>();
        var result = LinuxIntegration.Install(_dataHome, Exe, (tool, args) => { calls.Add($"{tool} {string.Join(' ', args)}"); return true; });

        Assert.True(result.Ok);
        Assert.Contains("*.tproj", File.ReadAllText(LinuxIntegration.MimePath(_dataHome)));
        var desktop = File.ReadAllText(LinuxIntegration.DesktopPath(_dataHome));
        Assert.Contains("Exec=\"/opt/Toucan App/Toucan\" %f", desktop);
        Assert.Contains($"MimeType={LinuxIntegration.MimeType};", desktop);
        Assert.Contains(calls, c => c.StartsWith("update-mime-database ", StringComparison.Ordinal));
        Assert.Contains($"xdg-mime default {LinuxIntegration.DesktopFileName} {LinuxIntegration.MimeType}", calls);
    }

    [Fact]
    public void LinuxInstall_StillSucceedsWhenTheDesktopToolsAreMissing()
    {
        var result = LinuxIntegration.Install(_dataHome, Exe, (_, _) => false);

        Assert.True(result.Ok);
        Assert.True(LinuxIntegration.IsInstalled(_dataHome, Exe));
    }

    [Fact]
    public void LinuxIsInstalled_IsFalseUntilInstalled_AndWhenTheAppMoved()
    {
        Assert.False(LinuxIntegration.IsInstalled(_dataHome, Exe));

        LinuxIntegration.Install(_dataHome, Exe, (_, _) => true);

        Assert.True(LinuxIntegration.IsInstalled(_dataHome, Exe));
        Assert.False(LinuxIntegration.IsInstalled(_dataHome, "/somewhere/else/Toucan"));
    }

    [Fact]
    public void LinuxUninstall_RemovesBothFiles_AndIsSafeToRepeat()
    {
        LinuxIntegration.Install(_dataHome, Exe, (_, _) => true);

        Assert.True(LinuxIntegration.Uninstall(_dataHome, (_, _) => true).Ok);
        Assert.False(File.Exists(LinuxIntegration.DesktopPath(_dataHome)));
        Assert.False(File.Exists(LinuxIntegration.MimePath(_dataHome)));
        Assert.True(LinuxIntegration.Uninstall(_dataHome, (_, _) => true).Ok);
    }

    [Theory]
    [InlineData("/usr/bin/toucan", "\"/usr/bin/toucan\"")]
    [InlineData("/opt/My$App/100%/t", "\"/opt/My\\$App/100%%/t\"")]
    [InlineData("/a\"b/t", "\"/a\\\"b/t\"")]
    public void ExecValue_QuotesAndEscapesPerTheDesktopEntrySpec(string exe, string expected) =>
        Assert.Equal(expected, LinuxIntegration.ExecValue(exe));

    // --- Integration page --------------------------------------------------------------------------------------

    [Fact]
    public void IntegrationPageSitsBeforeDataAndPrivacy() =>
        Assert.Equal("Integration", OptionsViewModel.Pages[Array.IndexOf(OptionsViewModel.Pages.ToArray(), "Data & Privacy") - 1]);

    [AvaloniaFact]
    public void IntegrationPageRenders()
    {
        using var host = new TestHost();
        var vm = host.Services.GetRequiredService<OptionsViewModel>();
        vm.SelectedPageIndex = vm.PageIndexOf("Integration");

        var dialog = new OptionsDialog(vm);
        dialog.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(dialog.CaptureRenderedFrame());
        dialog.Close();
    }

    [AvaloniaFact]
    public void OptionsDialog_ShowsExactlyOnePagePerPageIndex()
    {
        using var host = new TestHost();
        var vm = host.Services.GetRequiredService<OptionsViewModel>();
        var dialog = new OptionsDialog(vm);
        dialog.Show();

        var pages = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog)
            .OfType<global::Avalonia.Controls.Panel>()
            .Single(p => p.MaxWidth == 600 && p.Children.Count >= OptionsViewModel.Pages.Count);
        Assert.Equal(OptionsViewModel.Pages.Count, pages.Children.Count);
        for (var i = 0; i < OptionsViewModel.Pages.Count; i++)
        {
            vm.SelectedPageIndex = i;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(pages.Children.Count(c => c.IsVisible) == 1, $"page {i} ({OptionsViewModel.Pages[i]}) overlaps another page");
        }
        dialog.Close();
    }

    // --- Machine Translation panel -----------------------------------------------------------------------------

    [AvaloniaFact]
    public void ProviderChoices_ListEveryRegisteredProvider_AndSelectTheLastUsedOne()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();

        Assert.Contains(vm.ProviderChoices, c => c.Name == "DeepL");
        var selected = Assert.Single(vm.ProviderChoices, c => c.IsSelected);
        Assert.Equal(vm.AppOptions.LastProvider, selected.Name);
    }

    [AvaloniaFact]
    public void SelectingAProvider_PersistsItAndMovesTheSelection()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        var deepl = vm.ProviderChoices.First(c => c.Name == "DeepL");

        vm.SelectProviderCommand.Execute(deepl);

        Assert.Equal("DeepL", vm.AppOptions.LastProvider);
        Assert.Equal("DeepL", vm.SelectedProviderName);
        Assert.Equal(["DeepL"], vm.ProviderChoices.Where(c => c.IsSelected).Select(c => c.Name));
    }

    [AvaloniaFact]
    public void AProviderThatNoLongerExists_FallsBackToTheFirstAvailable()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        vm.AppOptions.LastProvider = "UninstalledPluginProvider";

        vm.RefreshProviderChoices();

        Assert.Equal(vm.ProviderChoices[0].Name, vm.SelectedProviderName);
        Assert.True(vm.ProviderChoices[0].IsSelected);
    }

    [AvaloniaFact]
    public async Task TranslatingAKey_ShowsTheProviderResultInTheLastRun()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("mt", ("en", """{"app.title": "My App"}"""), ("fr", """{"app.title": ""}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.AppOptions.LastProvider = "Mock"; // not listed in the panel (no settings definition) but still usable

        await vm.TranslateKeyAsync("app.title");

        Assert.True(vm.HasRecentTranslations);
        var item = Assert.Single(vm.RecentTranslations);
        Assert.True(item.Succeeded);
        Assert.Equal("app.title · fr", item.Heading);
        Assert.False(string.IsNullOrEmpty(vm.LastTranslationSummary));

        vm.ClearRecentTranslationsCommand.Execute(null);
        Assert.False(vm.HasRecentTranslations);
    }

    [AvaloniaFact]
    public async Task PreTranslateDialog_OffersTheRegisteredProviders_AndItsChoiceCarriesToThePanel()
    {
        using var host = new TestHost();
        var folder = host.CreateJsonProject("pre", ("en", """{"a": "A"}"""), ("fr", """{"a": ""}"""));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        IReadOnlyList<string>? offered = null;
        host.Dialogs.OnPreTranslate = dialog =>
        {
            offered = dialog.ProviderNames;
            dialog.SelectedProvider = "DeepL";
        };

        await vm.PreTranslateBulkCommand.ExecuteAsync(null);

        Assert.NotNull(offered);
        Assert.Equal(vm.ProviderChoices.Select(c => c.Name), offered!.Take(vm.ProviderChoices.Count));
        Assert.Equal("DeepL", vm.AppOptions.LastProvider);
        Assert.Equal(["DeepL"], vm.ProviderChoices.Where(c => c.IsSelected).Select(c => c.Name));
    }

    [AvaloniaFact]
    public async Task MachineTranslationPanel_ShowsInTheMainWindowsRightSlot()
    {
        using var host = new TestHost();
        global::Toucan.Avalonia.App.RegisterSidePanels();
        var vm = host.CreateViewModel();
        var window = new global::Toucan.Avalonia.Views.MainWindow(vm, host.Services.GetRequiredService<StatusBarViewModel>());
        window.Show();
        await vm.OpenProjectAsync(host.CreateJsonProject("win", ("en", """{"app.title": "My App"}"""), ("fr", """{"app.title": ""}""")));
        try
        {
            Toucan.Core.Services.SidePanelRegistry.Instance.RightSlotVisible = true;
            Toucan.Core.Services.SidePanelRegistry.Instance.Activate("machine-translation");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var panel = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<MachineTranslationPanel>().Single();
            Assert.True(panel.IsEffectivelyVisible);
            Assert.Same(vm, panel.DataContext);
            Assert.NotNull(window.CaptureRenderedFrame());
        }
        finally
        {
            Toucan.Core.Services.SidePanelRegistry.Instance.Activate("inspector"); // process-wide singleton
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MachineTranslationPanelRenders()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        var window = new global::Avalonia.Controls.Window { Width = 320, Height = 600, Content = new MachineTranslationPanel { DataContext = vm } };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.CaptureRenderedFrame());
        window.Close();
    }
}
