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
            ThemeService.ApplyFontSize(vm.AppOptions.FontSize);
            RegisterSidePanels();

            var statusBar = _services.GetRequiredService<StatusBarViewModel>();
            StatusBarService.Instance.Register(statusBar);
            statusBar.DefaultLanguage = vm.AppOptions.DefaultLanguage ?? "en-US";

            var window = new MainWindow(vm, statusBar);
            desktop.MainWindow = window;
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
                if (!string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path)))
                    await vm.OpenProjectAsync(path);
                // After the project is up, so a pending-plugins question never delays opening it.
                await PromptForPendingPluginsAsync(vm);
            };
        }

        base.OnFrameworkInitializationCompleted();
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
        registry.Register(new BuiltInSidePanel("explorer", "Explorer", "OpenFolder", SidePanelSlot.Left, 10));
        registry.Register(new BuiltInSidePanel("search", "Search", "Find", SidePanelSlot.Left, 20));
        registry.Register(new BuiltInSidePanel("issues", "Issues", "Important", SidePanelSlot.Left, 30));
        registry.Register(new BuiltInSidePanel("source-code", "Source Code", "Code", SidePanelSlot.Left, 40));
        registry.Register(new BuiltInSidePanel("languages", "Languages", "Globe", SidePanelSlot.Right, 10));
        registry.Register(new BuiltInSidePanel("inspector", "Inspector", "ContactInfo", SidePanelSlot.Right, 20));
        registry.Register(new BuiltInSidePanel("machine-translation", "Translation", "Character", SidePanelSlot.Right, 25));
        registry.Register(new BuiltInSidePanel("translation-memory", "Memory", "Library", SidePanelSlot.Right, 30));
        PanelService.Instance.RestoreActivePanels();
    }

    /// <param name="overrides">Test hook: registrations applied last, so they win over the defaults.</param>
    /// <param name="pluginRoot">Test hook: plugin folder to scan instead of Documents/Toucan/plugins.</param>
    /// <param name="pluginPolicyPath">Test hook: plugin enable/trust file instead of Documents/Toucan/plugin-policy.json.</param>
    internal static ServiceProvider ConfigureServices(Action<ServiceCollection>? overrides = null, string? pluginRoot = null, string? pluginPolicyPath = null)
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
        services.AddSingleton<IProjectDefaultsService, ProjectDefaultsService>();
        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<IProviderSettingsService, ProviderSettingsService>();
        services.AddSingleton<IUndoRedoService, UndoRedoService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();

        // Formats, providers, validation, project service (shared with the CLI; plugins add to the same collection)
        services.AddToucanCore();

        // Plugins load only when enabled and trusted (decisions live in plugin-policy.json; Settings → Plugins edits them).
        var pluginPolicy = new FilePluginPolicyStore(pluginPolicyPath ?? FilePluginPolicyStore.DefaultPath());
        services.AddSingleton<IPluginPolicyStore>(pluginPolicy);
        var pluginOptions = new PluginHostOptions { Policy = pluginPolicy };
        pluginOptions.Roots.Add(pluginRoot ?? PluginHostOptions.DefaultRoot());
        services.AddToucanPlugins(pluginOptions);

        // Editor services
        services.AddSingleton<IBulkActionService, BulkActionService>();
        services.AddSingleton<BulkOperationService>();
        services.AddSingleton<IFuzzySearchService, FuzzySearchService>();
        services.AddSingleton<ISearchAndReplaceService, SearchAndReplaceService>();
        services.AddSingleton<ITranslationMemory, TranslationMemoryService>();
        services.AddSingleton<IPackageService, PackageService>();
        services.AddSingleton<ISourceCodeService, SourceCodeService>();
        services.AddSingleton<ITranslationAnalyzer, TranslationAnalyzerService>();

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
            sp.GetRequiredService<ILogger<ProjectLifecycleService>>()));

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
            sp.GetRequiredService<ITranslationProviderRegistry>()));
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<OptionsViewModel>();
        services.AddTransient<ProviderSettingsViewModel>();

        overrides?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
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
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan");
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
