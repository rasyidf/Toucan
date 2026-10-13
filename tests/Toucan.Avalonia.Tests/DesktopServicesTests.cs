using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Components;
using Toucan.Core.Commands;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Plugins;
using Toucan.Plugins.Desktop;
using Xunit;

namespace Toucan.Avalonia.Tests;

public sealed class DesktopServicesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-desktop-services-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private TestHost HostWithSample()
    {
        var dir = Path.Combine(_root, "plugins", "sample.tsv");
        Directory.CreateDirectory(dir);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SamplePlugin")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        new FilePluginPolicyStore(Path.Combine(_root, "plugin-policy.json")).Trust("sample.tsv", PluginHasher.Compute(dir));
        return new TestHost(_root);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    private static (DesktopContributions Contributions, DesktopLoadResult Result) LoadSampleDesktop(TestHost host)
    {
        var contributions = new DesktopContributions();
        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        var workspace = new PluginWorkspaceContext(host.CreateViewModel(), commands);
        var desktop = new DesktopHost(contributions, workspace);
        var result = Assert.Single(DesktopPluginLoader.LoadAll(host.Services.GetRequiredService<IPluginCatalog>(), contributions, new SidePanelRegistry(), workspace, desktop,
            commands, services: host.Services.GetServices<PluginServicesRegistration>()));
        return (contributions, result);
    }

    // ─── the real sample uses the services ───

    [AvaloniaFact]
    public async Task TheSamplesSettingsAreTypedValidatedAndStoredOutsideItsFolder()
    {
        using var host = HostWithSample();
        var registration = Assert.Single(host.Services.GetServices<PluginServicesRegistration>());
        var config = registration.Services.Configuration;

        Assert.Equal(6, config.Schema!.Fields.Count);
        Assert.Equal("Hello", config.GetValue<string>("greeting"));
        Assert.Equal(30, config.GetValue<int>("intervalSeconds"));
        Assert.Equal("safe", config.GetValue<string>("mode"));

        Assert.False((await config.SetAsync("intervalSeconds", 1)).IsValid);
        Assert.False((await config.SetAsync("serverUrl", "ftp://x")).IsValid);
        Assert.False((await config.SetAsync("greeting", "")).IsValid);
        Assert.True((await config.SetAsync("greeting", "Howdy")).IsValid);
        Assert.Equal("Howdy", config.GetValue<string>("greeting"));

        // Settings land in the plugin's data folder, not next to its code (which would change its trust hash).
        var dataFile = Path.Combine(_root, "plugin-data", "sample.tsv", "config.json");
        Assert.True(File.Exists(dataFile));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "plugins", "sample.tsv"), "config.json"));
        Assert.Equal(PluginTrustState.Trusted, new FilePluginPolicyStore(Path.Combine(_root, "plugin-policy.json")).GetTrust("sample.tsv", PluginHasher.Compute(Path.Combine(_root, "plugins", "sample.tsv"))));
    }

    [AvaloniaFact]
    public async Task TheSamplesStatusBarItemFollowsItsSettings()
    {
        using var host = HostWithSample();
        var (contributions, result) = LoadSampleDesktop(host);
        Assert.Equal(DesktopLoadStatus.Loaded, result.Status);

        var item = Assert.Single(contributions.LeftStatusItems);
        Assert.Equal("sample.tsv.status", item.Id);
        Assert.Equal("Hello", item.Text);
        Assert.True(item.IsShown);
        Assert.True(item.IsClickable);
        Assert.Equal("Sample plugin: click to write a stamp", item.DisplayToolTip);

        await host.Services.GetServices<PluginServicesRegistration>().Single().Services.Configuration.SetAsync("greeting", "Hi there");

        Assert.Equal("Hi there", item.Text);
    }

    [AvaloniaFact]
    public async Task RunningTheSamplesCommandShowsBackgroundWorkAndEndsWithANotification()
    {
        using var host = HostWithSample();
        var vm = host.CreateViewModel();
        var folder = host.CreateJsonProject("p", ("en", "{\"a\":\"A\"}"), ("fr", "{\"a\":\"B\"}"));
        await vm.OpenProjectAsync(folder);
        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        var notifications = host.Services.GetRequiredService<INotificationCenter>();
        var operations = host.Services.GetRequiredService<IBackgroundOperationService>();

        Assert.True(vm.HasProject, string.Join("|", host.Messages.Shown));
        Assert.True(commands.GetState("sample.tsv.stamp").CanRun, commands.GetState("sample.tsv.stamp").Reason + $" workspace={commands.HasWorkspace} vm={vm.CurrentPath}");
        var run = commands.ExecuteAsync("sample.tsv.stamp");
        await WaitUntil(() => operations.Active.Count == 1);
        Assert.Equal("Writing sample stamp", operations.Active[0].Title);

        Assert.Equal(CommandRunStatus.Completed, (await run).Status);
        Assert.Contains(notifications.History, n => n.Source == "sample.tsv" && n.Content.Severity == NotificationSeverity.Success);
        Assert.Contains("Hello", File.ReadAllText(Path.Combine(_root, "plugin-data", "sample.tsv", "stamps.log")), StringComparison.Ordinal);
        Assert.Empty(operations.Active);
    }

    [AvaloniaFact]
    public async Task ClosingTheProjectCancelsTheSamplesRunningOperation()
    {
        using var host = HostWithSample();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("p", ("en", "{\"a\":\"A\"}"), ("fr", "{\"a\":\"B\"}")));
        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        var operations = host.Services.GetRequiredService<IBackgroundOperationService>();

        var run = commands.ExecuteAsync("sample.tsv.stamp");
        await WaitUntil(() => operations.Active.Count == 1);
        var operation = operations.Active[0];
        await host.Services.GetRequiredService<IPluginActivationService>().CloseWorkspaceAsync(vm.CurrentPath);
        await operation.Completion;

        Assert.Equal(OperationStatus.Cancelled, operation.Status);
        await run;
    }

    // ─── status bar items ───

    private sealed class FakeDesktopPlugin(Action<IDesktopPluginContext> init) : IToucanDesktopPlugin
    {
        public void InitializeDesktop(IDesktopPluginContext context) => init(context);
    }

    private static DesktopLoadResult Run(Action<IDesktopPluginContext> init, DesktopContributions contributions, TestHost host)
    {
        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        var workspace = new PluginWorkspaceContext(host.CreateViewModel(), commands);
        return DesktopPluginLoader.Initialize("acme.sync", new FakeDesktopPlugin(init), contributions, new SidePanelRegistry(), workspace,
            new DesktopHost(contributions, workspace), commands, NullLogger.Instance, new PluginServicesRegistration("acme.sync", null!).Services);
    }

    [AvaloniaFact]
    public void StatusItemsSortByOrderWithinTheirSideAndNeedTheirPluginPrefix()
    {
        using var host = new TestHost();
        var contributions = new DesktopContributions();

        var result = Run(c =>
        {
            c.AddStatusBarItem(new StatusBarItemContribution { Id = "acme.sync.b", Title = "B", Text = "b", Order = 20 });
            c.AddStatusBarItem(new StatusBarItemContribution { Id = "acme.sync.a", Title = "A", Text = "a", Order = 10 });
            c.AddStatusBarItem(new StatusBarItemContribution { Id = "acme.sync.r", Title = "R", Text = "r", Side = StatusBarSide.Right });
        }, contributions, host);

        Assert.Equal(DesktopLoadStatus.Loaded, result.Status);
        Assert.Equal(["acme.sync.a", "acme.sync.b"], contributions.LeftStatusItems.Select(i => i.Id));
        Assert.Equal(["acme.sync.r"], contributions.RightStatusItems.Select(i => i.Id));

        var bad = Run(c => c.AddStatusBarItem(new StatusBarItemContribution { Id = "other.x", Title = "X", Text = "x" }), new DesktopContributions(), host);
        Assert.Contains("must start with the plugin ID", bad.Error);
    }

    [AvaloniaFact]
    public void ADesktopPartThatFailsAfterAddingAStatusItemLeavesNothingBehind()
    {
        using var host = new TestHost();
        var contributions = new DesktopContributions();

        var result = Run(c =>
        {
            c.AddStatusBarItem(new StatusBarItemContribution { Id = "acme.sync.a", Title = "A", Text = "a" });
            throw new InvalidOperationException("boom");
        }, contributions, host);

        Assert.Equal(DesktopLoadStatus.Failed, result.Status);
        Assert.Empty(contributions.LeftStatusItems);
    }

    [AvaloniaFact]
    public async Task AStatusItemChangedFromAnotherThreadUpdatesOnTheUiThread()
    {
        var item = new PluginStatusBarItem("acme.sync", "acme.sync.a", "A", StatusBarSide.Left, 10, null);
        Assert.False(item.IsShown); // nothing to show yet
        var threads = new List<int>();
        item.PropertyChanged += (_, _) => threads.Add(Environment.CurrentManagedThreadId);
        var uiThread = Environment.CurrentManagedThreadId;

        await Task.Run(() => item.Text = "Syncing");
        await WaitUntil(() => threads.Count > 0);

        Assert.True(item.IsShown);
        Assert.All(threads, t => Assert.Equal(uiThread, t));
        Assert.True(item.HasText);
        item.Badge = "3";
        item.BadgeSeverity = StatusBarSeverity.Warning;
        item.IsVisible = false;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(item.HasBadge);
        Assert.True(item.IsBadgeWarn);
        Assert.False(item.IsShown);
    }

    // ─── background operations in the status bar ───

    [AvaloniaFact]
    public async Task TheStatusBarSummarisesBackgroundWorkAndClearsWhenItEnds()
    {
        var service = new BackgroundOperationService();
        var vm = new OperationsViewModel(service);
        Assert.False(vm.HasActive);
        var gate = new TaskCompletionSource();

        var one = service.Start("acme.sync", new OperationOptions { Title = "Pull" }, async (ctx, _) => { ctx.Report("12 of 30", 0.4); await gate.Task; });
        await WaitUntil(() => vm.HasActive && vm.Summary.Contains("12 of 30", StringComparison.Ordinal));
        Assert.Equal("Pull: 12 of 30", vm.Summary);
        Assert.Contains("40", vm.ToolTip, StringComparison.Ordinal);

        var two = service.Start("acme.sync", new OperationOptions { Title = "Push" }, (_, ct) => Task.Delay(Timeout.Infinite, ct));
        await WaitUntil(() => vm.Items.Count == 2);
        Assert.Equal("2 tasks running", vm.Summary);

        two.Cancel();
        gate.SetResult();
        await Task.WhenAll(one.Completion, two.Completion);
        await WaitUntil(() => !vm.HasActive);
        Assert.Equal(string.Empty, vm.Summary);
    }

    // ─── generated settings form ───

    [AvaloniaFact]
    public async Task TheSettingsFormIsGeneratedFromTheSchemaAndStoresWhatIsValid()
    {
        using var host = HostWithSample();
        var config = host.Services.GetServices<PluginServicesRegistration>().Single().Services.Configuration;
        var form = new ConfigForm(config, () => null);
        var window = new Window { Content = form, Width = 700, Height = 600 };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Five app-wide fields; the per-project one waits for a project.
        Assert.Equal(5, form.GetVisualDescendants().OfType<SettingsRow>().Count());
        Assert.Contains(form.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Settings for a project appear here while one is open.");

        var greeting = form.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == "Hello");
        var elsewhere = form.GetVisualDescendants().OfType<ComboBox>().Single();
        void Commit(TextBox box, string text)
        {
            box.Focus();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            box.Text = text;
            elsewhere.Focus(); // leaving the box is what stores its value
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Commit(greeting, "");
        await WaitUntil(() => form.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible && t.Text?.Contains("required", StringComparison.OrdinalIgnoreCase) == true));
        Assert.Equal("Hello", config.GetValue<string>("greeting")); // refused, so unchanged

        Commit(greeting, "Hey");
        await WaitUntil(() => config.GetValue<string>("greeting") == "Hey");

        var mode = form.GetVisualDescendants().OfType<ComboBox>().Single();
        mode.SelectedIndex = 1;
        await WaitUntil(() => config.GetValue<string>("mode") == "fast");

        var secret = form.GetVisualDescendants().OfType<TextBox>().Single(t => t.PasswordChar == '•');
        Commit(secret, "form-secret-value-1");
        await WaitUntil(() => config.GetSecretAsync("apiKey").Result == "form-secret-value-1");
        await WaitUntil(() => string.IsNullOrEmpty(secret.Text)); // never shown again
        Assert.DoesNotContain("form-secret-value-1", File.ReadAllText(Path.Combine(_root, "plugin-data", "sample.tsv", "config.json")), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public void ProjectSettingsAppearWhileAProjectIsOpen()
    {
        using var host = HostWithSample();
        var config = host.Services.GetServices<PluginServicesRegistration>().Single().Services.Configuration;

        var form = new ConfigForm(config, () => "/projects/one");
        new Window { Content = form }.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(6, form.GetVisualDescendants().OfType<SettingsRow>().Count());
        Assert.DoesNotContain(form.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Settings for a project appear here while one is open.");
    }

    // ─── toasts ───

    private static async Task<List<NotificationCard>> Cards(Window window)
    {
        await WaitUntil(() => window.GetVisualDescendants().OfType<NotificationCard>().Any());
        return [.. window.GetVisualDescendants().OfType<NotificationCard>()];
    }

    [AvaloniaFact]
    public async Task NotificationsShowAsToastsWithTheirSeverity()
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        using var host = new TestHost();
        using var presenter = new NotificationPresenter(window, host.Services.GetRequiredService<INotificationCenter>(), host.Services.GetRequiredService<ICommandRegistry>());

        host.Services.GetRequiredService<INotificationCenter>().Publish("acme.sync", new PluginNotification
        {
            Title = "Pull finished", Message = "12 keys updated", Severity = NotificationSeverity.Success,
        });
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var card = Assert.Single(await Cards(window));
        Assert.Equal(NotificationType.Success, ((Notification)card.Content!).Type);
        Assert.Equal("Pull finished", ((Notification)card.Content!).Title);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ANotificationFromAnotherThreadStillShowsOnTheUiThread()
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        using var host = new TestHost();
        var center = host.Services.GetRequiredService<INotificationCenter>();
        using var presenter = new NotificationPresenter(window, center, host.Services.GetRequiredService<ICommandRegistry>());

        await Task.Run(() => center.Publish("acme.sync", new PluginNotification { Title = "From a worker", Severity = NotificationSeverity.Warning }));
        await WaitUntil(() => window.GetVisualDescendants().OfType<NotificationCard>().Any());

        window.Close();
    }
}
