using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Toucan.Contracts.Services;
using Toucan.Contracts.Views;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services;
using Toucan.Models;
using Toucan.Services;
using Toucan.ViewModels;
using Toucan.Views;

namespace Toucan;

// For more information about application lifecycle events see https://docs.microsoft.com/dotnet/framework/wpf/app-development/application-management-overview

// WPF UI elements use language en-US by default.
// If you need to support other cultures make sure you add converters and review dates and numbers in your UI to ensure everything adapts correctly.
// Tracking issue for improving this is https://github.com/dotnet/wpf/issues/1946
public partial class App : Application
{
    private IHost _host;

    public T GetService<T>()
        where T : class
        => _host.Services.GetService(typeof(T)) as T;

    public App()
    {
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var appLocation = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        _host = Host.CreateDefaultBuilder(e.Args)
                .ConfigureAppConfiguration(c =>
                {
                    c.SetBasePath(appLocation);
                })
                .ConfigureServices(ConfigureServices)
                .Build();

        await _host.StartAsync();
    }

        // TODO: Register your services, viewmodels and pages here
    private void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {

        // App Host
        services.AddHostedService<ApplicationHostService>();

        // Activation Handlers

        // Core Services
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IProjectService, ProjectService>();
        // Save strategies
        services.AddSingleton<ISaveStrategy, Toucan.Core.Services.SaveStrategies.JsonSaveStrategy>();
        services.AddSingleton<ISaveStrategy, Toucan.Core.Services.SaveStrategies.NamespacedSaveStrategy>();

        // Services
        services.AddSingleton<IWindowManagerService, WindowManagerService>();
        services.AddSingleton<IApplicationInfoService, ApplicationInfoService>();
        services.AddSingleton<ISystemService, SystemService>();
        services.AddSingleton<IPersistAndRestoreService, PersistAndRestoreService>();
        services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
        services.AddSingleton<IPageService, PageService>();
        services.AddSingleton<INavigationService, NavigationService>();

        //services.AddSingleton<IProjectService, ProjectService>();
        //services.AddSingleton<ITranslationService, TranslationService>();
        //services.AddSingleton<IAppPreferenceService, AppPreferenceService>();
        //services.AddSingleton<IWebTranslationService, WebTranslationService>();
        //services.AddSingleton<INotificationService, NotificationService>();
        //services.AddSingleton<IValidationService, ValidationService>(); 

        // Views and ViewModels

        services.AddTransient<HomeViewModel>();
        services.AddTransient<HomePage>();

        services.AddTransient<ProjectViewModel>();
        services.AddTransient<ProjectPage>();

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsPage>();

        services.AddTransient<TranslationEditorViewModel>();
        services.AddTransient<TranslationEditorPage>();

        services.AddTransient<NamespaceViewModel>();
        services.AddTransient<NamespacePage>();

        services.AddTransient<SearchViewModel>();
        services.AddTransient<SearchPage>();

        services.AddTransient<IShellWindow, ShellWindow>();
        services.AddTransient<ShellViewModel>();

        services.AddTransient<IShellDialogWindow, ShellDialogWindow>();
        services.AddTransient<ShellDialogViewModel>();

        // Configuration
        //services.Configure<AppConfig>(context.Configuration.GetSection(nameof(AppConfig)));

        services.AddSingleton<IAppConfigService, AppConfigService>();
        services.AddSingleton<IRightPaneService, RightPaneService>();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        _host = null;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // TODO: Please log and handle the exception as appropriate to your scenario
        // For more info see https://docs.microsoft.com/dotnet/api/system.windows.application.dispatcherunhandledexception?view=netcore-3.0
    }
}
