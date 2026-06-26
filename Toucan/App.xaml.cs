using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts;
// using Toucan.Core.Contracts.Services; // already used inside other files; unnecessary duplicate
using Toucan.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Toucan.ViewModels;
using Toucan.Core.Services;
using Toucan.Core.Services.SaveStrategies;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Contracts.Services;

namespace Toucan;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
        private static void ReportUnhandledException(Exception ex, string source)
        {
            try
            {
                // Attempt to write to console and show a message box for local debugging
                Console.Error.WriteLine($"Unhandled exception ({source}): {ex}");
                // also append to a log file to help capture stack traces from CI or dev machines
                try
                {
                    var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "toucan-unhandled-exceptions.log");
                    System.IO.File.AppendAllText(logPath, $"\n=== {DateTime.UtcNow:u} ({source}) ===\n{ex}\n");
                }
                catch (Exception) { }
                System.Windows.MessageBox.Show($"Unhandled exception ({source}): {ex.Message}\n\nSee console or logs for full details.", "Unhandled exception", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            catch (Exception) { }
        }
    public static IServiceProvider Services { get; private set; }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
            // Global exception handlers to capture runtime issues (helps debug crashes like AllowsTransparency errors)
            this.DispatcherUnhandledException += (s, ea) =>
            {
                ReportUnhandledException(ea.Exception, "DispatcherUnhandledException");
                ea.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, ea) =>
            {
                if (ea.ExceptionObject is Exception ex)
                    ReportUnhandledException(ex, "AppDomain.UnhandledException");
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ea) =>
            {
                ReportUnhandledException(ea.Exception, "TaskScheduler.UnobservedTaskException");
                ea.SetObserved();
            };
        string startupPath = "";

        if (e.Args.Length == 1)
        {
            startupPath = e.Args[0];
        }

        var services = new ServiceCollection();

        // Add logging and configure console logger for dev-time visibility
        services.AddLogging(builder => builder.AddConsole());

        // register simple services (UI helpers)
        services.AddSingleton<IRecentProjectService, RecentProjectService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IPreferenceService, PreferenceService>();
        
        // provider settings + secure storage for API keys
        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<IProviderSettingsService, ProviderSettingsService>();

        // Secure storage and provider settings
        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<IProviderSettingsService, ProviderSettingsService>();

        // core file service
        services.AddSingleton<IFileService, FileService>();

        // Bulk action service
        services.AddSingleton<IBulkActionService, BulkActionService>();

        // Pretranslation engine and a simple mock provider
        services.AddSingleton<IPretranslationService, PretranslationService>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.MockTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.DeepLTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.GoogleTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.MicrosoftTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.OpenAITranslationProvider>();

        // Save Strategies - register concrete types and interface mappings
        services.AddSingleton<JsonSaveStrategy>();
        services.AddSingleton<NamespacedSaveStrategy>();
        services.AddSingleton<PoSaveStrategy>();
        services.AddSingleton<IniSaveStrategy>();
        services.AddSingleton<YamlSaveStrategy>();
        services.AddSingleton<TomlSaveStrategy>();
        services.AddSingleton<AndroidXmlSaveStrategy>();
        services.AddSingleton<IosStringsSaveStrategy>();
        services.AddSingleton<XliffSaveStrategy>();
        services.AddSingleton<ArbSaveStrategy>();
        services.AddSingleton<CsvSaveStrategy>();
        services.AddSingleton<ResxSaveStrategy>();

        services.AddSingleton<JavaPropertiesSaveStrategy>();

        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<JsonSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<NamespacedSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<PoSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<IniSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<YamlSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<TomlSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<AndroidXmlSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<IosStringsSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<XliffSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<ArbSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<CsvSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<ResxSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<JavaPropertiesSaveStrategy>());

        // Load strategies - register concrete types and interface mappings
        services.AddSingleton<JsonLoadStrategy>();
        services.AddSingleton<NamespacedLoadStrategy>();
        services.AddSingleton<ManifestLoadStrategy>();
        services.AddSingleton<YamlLoadStrategy>();
        services.AddSingleton<TomlLoadStrategy>();
        services.AddSingleton<AndroidXmlLoadStrategy>();
        services.AddSingleton<IosStringsLoadStrategy>();
        services.AddSingleton<XliffLoadStrategy>();
        services.AddSingleton<ArbLoadStrategy>();
        services.AddSingleton<CsvLoadStrategy>();
        services.AddSingleton<ResxLoadStrategy>();
        services.AddSingleton<PoLoadStrategy>();
        services.AddSingleton<JavaPropertiesLoadStrategy>();

        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<JsonLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<NamespacedLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<ManifestLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<YamlLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<TomlLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<AndroidXmlLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<IosStringsLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<XliffLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<ArbLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<CsvLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<ResxLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<PoLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<JavaPropertiesLoadStrategy>());

        // Strategy factory and mode resolver
        services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();
        services.AddSingleton<IProjectModeResolver, ProjectModeResolver>();

        // Project service
        services.AddSingleton<IProjectService, ProjectService>();

        // Register MainWindowViewModel and other UI ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<NewProjectPrompt>();

        // Register remaining ViewModels (transient by default so each window/dialog gets a fresh instance)
        services.AddTransient<LanguagePromptViewModel>();
        services.AddTransient<LanguageSummaryItemViewModel>();
        services.AddTransient<OptionsViewModel>();
        services.AddTransient<PreTranslateViewModel>();
        services.AddTransient<ProviderSettingsViewModel>();
        services.AddTransient<StartScreenViewModel>();
        services.AddTransient<TranslationItemViewModel>();

        // Factory for creating TranslationItemViewModel with a TranslationItem param
        services.AddTransient<System.Func<Toucan.Core.Models.TranslationItem, TranslationItemViewModel>>(sp => ti => ActivatorUtilities.CreateInstance<TranslationItemViewModel>(sp, ti));

        // Register StatusBarViewModel as a singleton so any part of the app that requests it
        // can share the same instance (status is app-global)
        services.AddSingleton<StatusBarViewModel>();

        // Factories for viewmodels that require runtime parameters so code can resolve instances via DI
        services.AddTransient<Func<string, LanguageGroupViewModel>>(sp => ns => ActivatorUtilities.CreateInstance<LanguageGroupViewModel>(sp, ns));
        services.AddTransient<Func<System.Windows.Window, AboutViewModel>>(sp => wnd => ActivatorUtilities.CreateInstance<AboutViewModel>(sp, wnd));
        services.AddTransient<Func<Toucan.Core.Models.Project, System.Action<string>, RecentProjectViewModel>>(sp => (proj, act) => ActivatorUtilities.CreateInstance<RecentProjectViewModel>(sp, proj, act));
        services.AddTransient<Func<System.Collections.Generic.IEnumerable<Toucan.Core.Models.TranslationItem>, LanguagePromptViewModel>>(sp => list => ActivatorUtilities.CreateInstance<LanguagePromptViewModel>(sp, list));
        // Factory for PreTranslateViewModel (languages + sourceItems + optional IPretranslationService)
        services.AddTransient<Func<System.Collections.Generic.IEnumerable<string>, System.Collections.Generic.IEnumerable<Toucan.Core.Models.TranslationItem>, Toucan.Core.Contracts.Services.IPretranslationService, PreTranslateViewModel>>(
            sp => (langs, items, svc) => ActivatorUtilities.CreateInstance<PreTranslateViewModel>(sp, langs, items, svc));

        var serviceProvider = services.BuildServiceProvider();
        Services = serviceProvider;

        var viewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
        var mainWindow = new MainWindow(startupPath, viewModel);
        mainWindow.Show();


    }
}
