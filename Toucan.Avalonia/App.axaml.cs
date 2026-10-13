using Toucan.Modules;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Avalonia.Views.Panels;
using FluentAvalonia.UI.Controls;
using Toucan.Core.Services;

namespace Toucan.Avalonia;

public partial class App : Application
{
    private IServiceProvider? _services;

    /// <summary>Dialog service for view code that isn't constructed through DI (menus).</summary>
    internal static IDialogService Dialogs { get; private set; } = null!;

    public override void Initialize()
    {
        // Before the XAML loads, so literal text is looked up in the chosen language. Tests set one explicitly first.
        if (!Locales.Loc.IsInitialized) Locales.Loc.Use(Toucan.Core.Options.AppOptions.LoadFromDisk().AppLanguage);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogException(e.Exception, "UnobservedTaskException");
                e.SetObserved();
            };

            _services = ConfigureServices();
            Dialogs = _services.GetRequiredService<IDialogService>();

            var vm = _services.GetRequiredService<MainWindowViewModel>();
            ThemeService.Apply(vm.AppOptions.Theme);
            ColorSchemeService.Apply(vm.AppOptions);
            ThemeService.ApplyFontSize(vm.AppOptions.FontSize);
            RegisterSidePanels();
            LoadDesktopPlugins(vm);

            var statusBar = _services.GetRequiredService<StatusBarViewModel>();
            StatusBarService.Instance.Register(statusBar);
            statusBar.DefaultLanguage = vm.AppOptions.DefaultLanguage ?? "en-US";

            KeybindingService.UseRegistry(_services.GetRequiredService<global::Toucan.Core.Commands.ICommandRegistry>());
            var window = new MainWindow(vm, statusBar);
            desktop.MainWindow = window;
            var notifications = new NotificationPresenter(window, _services.GetRequiredService<INotificationCenter>(),
                _services.GetRequiredService<global::Toucan.Core.Commands.ICommandRegistry>());
            window.Closed += (_, _) => notifications.Dispose();
            desktop.ShutdownMode = global::Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            var messages = _services.GetRequiredService<IAsyncMessageService>();
            var lifecycle = (ProjectLifecycleService)_services.GetRequiredService<IProjectLifecycleService>();
            lifecycle.SetUnsavedChangesHandler(new UnsavedChangesHandler(messages));
            lifecycle.SetExternalChangeHandler(new ExternalChangeHandler(messages));
            // Reloads and merges run on the UI thread and then refresh the editor from the store.
            lifecycle.SetUiDispatcher(work => Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await work();
                if (vm.HasProject) vm.LoadFromStore(vm.CurrentPath);
            }));

            var startupPath = desktop.Args is { Length: > 0 } args ? args[0] : null;

            // macOS hands Finder "open with" requests to the running app as an activation event, not as arguments.
            var activatedByFile = false;
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is not FileActivatedEventArgs { Files.Count: > 0 } files) return;
                    var opened = files.Files[0].TryGetLocalPath();
                    if (string.IsNullOrEmpty(opened) || !(Directory.Exists(opened) || File.Exists(opened))) return;
                    activatedByFile = true;
                    Dispatcher.UIThread.Post(() => _ = vm.OpenProjectAsync(opened));
                };
            }

            window.Opened += async (_, _) =>
            {
                var path = startupPath;
                if (string.IsNullOrEmpty(path) && !activatedByFile && vm.AppOptions.OpenLastProjectOnStartup) path = vm.AppOptions.LastProjectPath;
                // Before the project opens: on first run the user decides whether to use AI.
                await vm.RunOnboardingIfNeededAsync();
                if (!string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path)))
                    await vm.OpenProjectAsync(path);
                // After the project is up, so a pending-plugins question never delays opening it.
                await PromptForPendingPluginsAsync(vm);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Loads the desktop part of each plugin that loaded. Failures are recorded next to the plugin and never stop startup.</summary>
    private void LoadDesktopPlugins(MainWindowViewModel vm)
    {
        if (_services?.GetService<IPluginCatalog>() is not { } catalog) return;
        var commands = _services.GetRequiredService<global::Toucan.Core.Commands.ICommandRegistry>();
        var workspace = new PluginWorkspaceContext(vm, commands);
        var host = new DesktopHost(DesktopContributions.Instance, workspace);
        var registrations = _services.GetServices<PluginServicesRegistration>().ToList();
        DesktopContributions.Instance.Workspace = workspace;
        DesktopContributions.Instance.SetConfigurations(registrations
            .Where(r => r.Services.Configuration.Schema is { Fields.Count: > 0 })
            .Select(r => new PluginConfigurationEntry(r.PluginId, catalog.Plugins.FirstOrDefault(p => p.DisplayId == r.PluginId)?.Manifest?.Name ?? r.PluginId, r.Services.Configuration)));
        DesktopPluginLoader.LoadAll(catalog, DesktopContributions.Instance, SidePanelRegistry.Instance, workspace, host, commands,
            _services.GetService<ILoggerFactory>(), registrations);
    }

    private async Task PromptForPendingPluginsAsync(MainWindowViewModel vm)
    {
        try
        {
            if (_services?.GetService<IPluginCatalog>() is not { } catalog || _services.GetService<IPluginPolicyStore>() is not { } policy) return;
            var messages = _services.GetRequiredService<IAsyncMessageService>();
            await PluginPrompt.RunAsync(catalog, policy, messages, () => vm.ShowPluginsCommand.Execute(null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A failed nudge must never stop the project from opening.
            LogException(ex, "PluginPrompt");
        }
    }

    private void OnAboutClick(object? sender, EventArgs e) =>
        _services?.GetService<MainWindowViewModel>()?.HelpAboutCommand.Execute(null);

    private void OnPreferencesClick(object? sender, EventArgs e) =>
        _services?.GetService<MainWindowViewModel>()?.ShowPreferencesCommand.Execute(null);

    internal static void RegisterSidePanels()
    {
        var registry = SidePanelRegistry.Instance;
        var panels = DesktopContributions.Instance;

        // Each built-in panel registers like a plugin's would: an activity-bar entry, a factory and a toolbar.
        void Add(string id, string title, string icon, SidePanelSlot slot, int order, Func<MainWindowViewModel, global::Avalonia.Controls.Control> create,
            Func<MainWindowViewModel, IReadOnlyList<PanelActionItem>>? actions = null, Action<MainWindowViewModel>? onShow = null)
        {
            registry.Register(new BuiltInSidePanel(id, title, icon, slot, order));
            // Built-in panels bind straight to the main view model; plugin panels get no such data context.
            if (!panels.HasPanel(id)) panels.AddPanel(id, vm => { var panel = create(vm); panel.DataContext = vm; return panel; }, actions, onShow);
        }

        Add("explorer", "Explorer", "OpenFolder", SidePanelSlot.Left, 10, _ => new ExplorerPanel(), vm =>
        [
            new(FASymbol.Add, "Add translation key", vm.NewItemCommand),
            new(FASymbol.List, "Toggle tree / list", vm.ToggleViewModeCommand),
        ]);
        Add("search", "Search", "Find", SidePanelSlot.Left, 20, _ => new SearchPanel(), vm =>
            [new(FASymbol.Clear, "Clear search history", vm.ClearSearchHistoryCommand)]);
        Add("issues", "Issues", "Flag", SidePanelSlot.Left, 30, _ => new IssuesPanel(), vm =>
        [
            new(FASymbol.Refresh, "Run validation", vm.RunValidationCommand),
            new(FASymbol.Clear, "Dismiss all", vm.DismissAllIssuesCommand),
        ]);
        Add("source-code", "Source Code", "CodeHTML", SidePanelSlot.Left, 40, _ => new SourceCodePanel(), vm =>
        [
            new(FASymbol.Sync, "Scan source code", vm.ScanSourceCodeCommand),
            new(FASymbol.OpenFolder, "Choose source folder", vm.SelectSourceRootCommand),
        ]);
        Add("languages", "Languages", "Globe", SidePanelSlot.Right, 10, _ => new LanguagesPanel(), vm =>
        [
            new(FASymbol.Add, "Add language", vm.NewLanguageCommand),
            new(FASymbol.Setting, "Manage languages", vm.ManageLanguagesCommand),
        ]);
        Add("inspector", "Inspector", "Tag", SidePanelSlot.Right, 20, _ => new InspectorPanel(), vm =>
            [new(FASymbol.Character, "Translate selected key", vm.TranslateSelectedKeyCommand)]);
        Add("machine-translation", "Translation", "Character", SidePanelSlot.Right, 25, _ => new MachineTranslationPanel(), vm =>
        [
            new(FASymbol.Character, "Translate selected key", vm.TranslateSelectedKeyCommand),
            new(FASymbol.Setting, "Provider settings", vm.OpenProviderSettingsCommand),
        ], vm => vm.RefreshProviderChoices());
        Add("translation-memory", "Memory", "Library", SidePanelSlot.Right, 30, _ => new TranslationMemoryPanel(), vm =>
        [
            new(FASymbol.Import, "Import TMX", vm.ImportTmxCommand),
            new(FASymbol.SaveAs, "Export TMX", vm.ExportTmxCommand),
            new(FASymbol.Delete, "Clear translation memory", vm.ClearTmCommand),
        ]);
        PanelService.Instance.RestoreActivePanels();
    }

    /// <param name="overrides">Test hook: registrations applied last, so they win over the defaults.</param>
    /// <param name="pluginRoot">Test hook: plugin folder to scan instead of Documents/Toucan/plugins.</param>
    /// <param name="pluginPolicyPath">Test hook: plugin enable/trust file instead of Documents/Toucan/plugin-policy.json.</param>
    /// <param name="pluginDataRoot">Test hook: where plugins keep their own settings and files instead of Documents/Toucan/plugin-data.</param>
    internal static ServiceProvider ConfigureServices(Action<ServiceCollection>? overrides = null, string? pluginRoot = null, string? pluginPolicyPath = null, string? pluginDataRoot = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // UI services
        services.AddSingleton<IRecentProjectService, RecentProjectService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MessageService>();
        services.AddSingleton<IAsyncMessageService>(sp => sp.GetRequiredService<MessageService>());
        services.AddSingleton<IMessageService>(sp => sp.GetRequiredService<MessageService>());
        services.AddSingleton<IPreferenceService, PreferenceService>();
        services.AddSingleton<IProjectDefaultsService>(_ => new ProjectDefaultsService());
        services.AddSingleton<IProviderSettingsService>(sp => new ProviderSettingsService(
            sp.GetRequiredService<ISecretService>(), sp.GetRequiredService<ISecureStorageService>()));
        services.AddSingleton<IUndoRedoService, UndoRedoService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();

        // Formats, providers, validation, project service (shared with the CLI; plugins add to the same collection)
        services.AddToucanCore();
        services.AddToucanDefaults();

        // Plugins load only when enabled and trusted (decisions live in plugin-policy.json; Settings → Plugins edits them).
        var pluginPolicy = new FilePluginPolicyStore(pluginPolicyPath ?? FilePluginPolicyStore.DefaultPath());
        services.AddSingleton<IPluginPolicyStore>(pluginPolicy);
        var pluginOptions = new PluginHostOptions { Policy = pluginPolicy };
        if (pluginDataRoot is not null) pluginOptions.DataRoot = pluginDataRoot;
        pluginOptions.Roots.Add(pluginRoot ?? PluginHostOptions.DefaultRoot());
        // A plugin's desktop part must use the host's UI framework, never a copy of its own.
        foreach (var prefix in new[] { "Avalonia", "FluentAvalonia", "Toucan.Plugins.Avalonia", "MicroCom" }) pluginOptions.SharedAssemblyPrefixes.Add(prefix);
        services.AddToucanPlugins(pluginOptions);

        // Editor services
        services.AddSingleton<IBulkActionService, BulkActionService>();
        services.AddSingleton<BulkOperationService>();
        services.AddSingleton<IFuzzySearchService, FuzzySearchService>();
        services.AddSingleton<ISearchAndReplaceService, SearchAndReplaceService>();
        services.AddSingleton<ITranslationMemory, TranslationMemoryService>();
        services.AddSingleton<IPackageService, PackageService>();
        services.AddSingleton<ISourceCodeService, SourceCodeService>();

        // Project model
        services.AddSingleton<ITranslationManagementService, TranslationManagementService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<LanguageManagementService>();
        services.AddSingleton<ILanguageManagementService>(sp => sp.GetRequiredService<LanguageManagementService>());
        services.AddSingleton<IDiffMergeEngine, DiffMergeEngine>();
        services.AddSingleton<IAutoSaveService>(sp => new AutoSaveService(
            new Lazy<IProjectLifecycleService>(sp.GetRequiredService<IProjectLifecycleService>),
            sp.GetRequiredService<ITranslationManagementService>(),
            sp.GetRequiredService<IFileWatcherService>(),
            sp.GetRequiredService<ILogger<AutoSaveService>>()));
        services.AddSingleton<IProjectLifecycleService>(sp => new ProjectLifecycleService(
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<ITranslationManagementService>(),
            sp.GetRequiredService<IFileWatcherService>(),
            sp.GetRequiredService<IValidationPipeline>(),
            sp.GetRequiredService<IAutoSaveService>(),
            sp.GetRequiredService<IDiffMergeEngine>(),
            sp.GetRequiredService<IAuditService>(),
            sp.GetRequiredService<IRecentProjectService>(),
            sp.GetRequiredService<ICommentPersistenceService>(),
            sp.GetRequiredService<LanguageManagementService>(),
            null,
            null,
            sp.GetRequiredService<ILogger<ProjectLifecycleService>>(),
            sp.GetRequiredService<IRecoveryDraftService>(),
            sp.GetService<IPluginActivationService>()));

        // View models
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton(sp => new MainWindowServices(
            sp.GetRequiredService<IRecentProjectService>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IAsyncMessageService>(),
            sp.GetRequiredService<IPreferenceService>(),
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<IProjectLifecycleService>(),
            sp.GetRequiredService<ITranslationManagementService>(),
            sp.GetRequiredService<ILanguageManagementService>(),
            sp.GetRequiredService<ITranslationStrategyFactory>(),
            sp.GetRequiredService<IPretranslationService>(),
            sp.GetRequiredService<IProviderSettingsService>(),
            sp.GetRequiredService<IValidationPipeline>(),
            sp.GetRequiredService<ISourceCodeService>(),
            sp.GetRequiredService<ITranslationAnalyzer>(),
            sp.GetRequiredService<IUndoRedoService>(),
            sp.GetRequiredService<IFuzzySearchService>(),
            sp.GetRequiredService<ISearchAndReplaceService>(),
            sp.GetRequiredService<ITranslationMemory>(),
            sp.GetRequiredService<BulkOperationService>(),
            sp.GetRequiredService<ITranslationProviderRegistry>(),
            sp.GetRequiredService<IAiService>(),
            sp.GetRequiredService<IAiSettingsStore>(),
            sp.GetRequiredService<ISourceClarityService>(),
            sp.GetService<IDiagnosticsService>(),
            sp.GetService<IPluginCatalog>()));
        services.AddSingleton<MainWindowViewModel>();
        // Plugins read and edit the open project through this (see IWorkspaceApi); it resolves the view model on first use.
        services.AddSingleton<global::Toucan.Core.Plugins.IWorkspaceBackend>(sp => new WorkspaceBackend(sp.GetRequiredService<MainWindowViewModel>()));
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient(sp => new AiSettingsViewModel(
            sp.GetRequiredService<IAiSettingsStore>(),
            sp.GetRequiredService<ISecretService>(),
            sp.GetRequiredService<Toucan.Core.Services.Ai.AiService>(),
            sp.GetRequiredService<IPromptLibrary>(),
            sp.GetRequiredService<IDialogService>(),
            // The open project, so its own prompts can be edited; read when needed, after the main window exists.
            () => sp.GetService<MainWindowViewModel>() is { HasProject: true } main ? main.CurrentPath : null));
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<OptionsViewModel>();
        services.AddTransient<ProviderSettingsViewModel>();

        overrides?.Invoke(services);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        provider.UsePluginServices();
        return provider;
    }

    private void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception, "Dispatcher");
        e.Handled = true;
        _services?.GetService<IAsyncMessageService>()?.ShowMessage($"Something went wrong: {e.Exception.Message}\n\nDetails were written to the log.", "Unexpected Error");
    }

    private static void LogException(Exception ex, string source)
    {
        try
        {
            var dir = Path.Combine(Toucan.Core.Services.UserDataFolder.Root, "Toucan");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "toucan-avalonia-errors.log"), $"\n=== {DateTime.UtcNow:u} ({source}) ===\n{ex}\n");
        }
        catch (IOException)
        {
            // Logging must never throw.
        }
        Console.Error.WriteLine($"Unhandled exception ({source}): {ex}");
    }
}
