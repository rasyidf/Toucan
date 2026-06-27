using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Windows;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;
using Toucan.Services;
using Toucan.ViewModels;
using Toucan.Views;
using Toucan.Views.Dialogs;

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
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "toucan-unhandled-exceptions.log");
                System.IO.File.AppendAllText(logPath, $"\n=== {DateTime.UtcNow:u} ({source}) ===\n{ex}\n");
            }
            catch (Exception) { }
            _ = System.Windows.MessageBox.Show($"Unhandled exception ({source}): {ex.Message}\n\nSee console or logs for full details.", "Unhandled exception", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        catch (Exception) { }
    }

    private IServiceProvider _services = null!;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Global exception handlers to capture runtime issues (helps debug crashes like AllowsTransparency errors)
        DispatcherUnhandledException += (s, ea) =>
        {
            ReportUnhandledException(ea.Exception, "DispatcherUnhandledException");
            ea.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, ea) =>
        {
            if (ea.ExceptionObject is Exception ex)
            {
                ReportUnhandledException(ex, "AppDomain.UnhandledException");
            }
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
        _ = services.AddLogging(builder => builder.AddConsole());

        // register simple services (UI helpers)
        _ = services.AddSingleton<IRecentProjectService, RecentProjectService>();
        _ = services.AddSingleton<IDialogService, WpfDialogService>();
        _ = services.AddSingleton<IMessageService, MessageService>();
        _ = services.AddSingleton<IPreferenceService, PreferenceService>();

        // provider settings + secure storage for API keys
        _ = services.AddSingleton<ISecureStorageService, SecureStorageService>();
        _ = services.AddSingleton<IProviderSettingsService, ProviderSettingsService>();

        // core file service
        _ = services.AddSingleton<IFileService, FileService>();

        // Bulk action service
        _ = services.AddSingleton<IBulkActionService, BulkActionService>();

        // Pretranslation engine and a simple mock provider
        _ = services.AddSingleton<IPretranslationService, PretranslationService>();
        _ = services.AddSingleton<Toucan.Core.Contracts.ITranslationMemory, Toucan.Core.Services.TranslationMemoryService>();
        _ = services.AddSingleton<IPackageService, Toucan.Core.Services.PackageService>();
        _ = services.AddSingleton<Toucan.Core.Contracts.ISourceCodeService, Toucan.Core.Services.SourceCodeService>();
        _ = services.AddSingleton<Toucan.Core.Contracts.ITranslationAnalyzer, Toucan.Core.Services.TranslationAnalyzerService>();
        _ = services.AddSingleton<ITranslationProvider, Core.Services.Providers.MockTranslationProvider>();
        _ = services.AddSingleton<ITranslationProvider, Core.Services.Providers.DeepLTranslationProvider>();
        _ = services.AddSingleton<ITranslationProvider, Core.Services.Providers.GoogleTranslationProvider>();
        _ = services.AddSingleton<ITranslationProvider, Core.Services.Providers.MicrosoftTranslationProvider>();
        _ = services.AddSingleton<ITranslationProvider, Core.Services.Providers.OpenAITranslationProvider>();
        _ = services.AddSingleton<ITranslationProvider, Core.Services.Providers.CustomWebhookTranslationProvider>();

        // Save Strategies - register concrete types and interface mappings
        _ = services.AddSingleton<JsonSaveStrategy>();
        _ = services.AddSingleton<NamespacedSaveStrategy>();
        _ = services.AddSingleton<PoSaveStrategy>();
        _ = services.AddSingleton<IniSaveStrategy>();
        _ = services.AddSingleton<YamlSaveStrategy>();
        _ = services.AddSingleton<TomlSaveStrategy>();
        _ = services.AddSingleton<AndroidXmlSaveStrategy>();
        _ = services.AddSingleton<IosStringsSaveStrategy>();
        _ = services.AddSingleton<XliffSaveStrategy>();
        _ = services.AddSingleton<ArbSaveStrategy>();
        _ = services.AddSingleton<CsvSaveStrategy>();
        _ = services.AddSingleton<ResxSaveStrategy>();

        _ = services.AddSingleton<JavaPropertiesSaveStrategy>();

        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<JsonSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<NamespacedSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<PoSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<IniSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<YamlSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<TomlSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<AndroidXmlSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<IosStringsSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<XliffSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<ArbSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<CsvSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<ResxSaveStrategy>());
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<JavaPropertiesSaveStrategy>());

        _ = services.AddSingleton<LaravelPhpSaveStrategy>();
        _ = services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<LaravelPhpSaveStrategy>());

        // Load strategies - register concrete types and interface mappings
        _ = services.AddSingleton<JsonLoadStrategy>();
        _ = services.AddSingleton<NamespacedLoadStrategy>();
        _ = services.AddSingleton<ManifestLoadStrategy>();
        _ = services.AddSingleton<YamlLoadStrategy>();
        _ = services.AddSingleton<TomlLoadStrategy>();
        _ = services.AddSingleton<AndroidXmlLoadStrategy>();
        _ = services.AddSingleton<IosStringsLoadStrategy>();
        _ = services.AddSingleton<XliffLoadStrategy>();
        _ = services.AddSingleton<ArbLoadStrategy>();
        _ = services.AddSingleton<CsvLoadStrategy>();
        _ = services.AddSingleton<ResxLoadStrategy>();
        _ = services.AddSingleton<PoLoadStrategy>();
        _ = services.AddSingleton<JavaPropertiesLoadStrategy>();

        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<JsonLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<NamespacedLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<ManifestLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<YamlLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<TomlLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<AndroidXmlLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<IosStringsLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<XliffLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<ArbLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<CsvLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<ResxLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<PoLoadStrategy>());
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<JavaPropertiesLoadStrategy>());

        _ = services.AddSingleton<LaravelPhpLoadStrategy>();
        _ = services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<LaravelPhpLoadStrategy>());

        // Framework profiles
        _ = services.AddSingleton<Toucan.Core.Contracts.IFrameworkProfile, Toucan.Core.Services.Frameworks.GenericJsonProfile>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IFrameworkProfile, Toucan.Core.Services.Frameworks.I18nextProfile>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IFrameworkProfile, Toucan.Core.Services.Frameworks.AndroidProfile>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IFrameworkProfile, Toucan.Core.Services.Frameworks.FlutterArbProfile>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IFrameworkProfile, Toucan.Core.Services.Frameworks.DotNetResxProfile>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IFrameworkProfile, Toucan.Core.Services.Frameworks.IosProfile>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IFrameworkProfile, Toucan.Core.Services.Frameworks.GettextProfile>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IFrameworkProfile, Toucan.Core.Services.Frameworks.RailsYamlProfile>();

        // Validation pipeline
        _ = services.AddSingleton<Toucan.Core.Contracts.IValidationRule, Toucan.Core.Services.Validation.MissingTranslationRule>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IValidationRule, Toucan.Core.Services.Validation.PlaceholderMismatchRule>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IValidationRule, Toucan.Core.Services.Validation.DuplicateKeyRule>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IValidationRule, Toucan.Core.Services.Validation.UntranslatedCopyRule>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IValidationRule, Toucan.Core.Services.Validation.EmptyValueRule>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IValidationRule, Toucan.Core.Services.Validation.WhitespaceMismatchRule>();
        _ = services.AddSingleton<Toucan.Core.Contracts.IValidationPipeline, Toucan.Core.Services.Validation.ValidationPipeline>();

        // Strategy factory and mode resolver
        _ = services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();
        _ = services.AddSingleton<IProjectModeResolver, ProjectModeResolver>();

        // Project service
        _ = services.AddSingleton<IProjectService, ProjectService>();

        // Register MainWindowViewModel and other UI ViewModels
        _ = services.AddTransient<MainWindowViewModel>();
        _ = services.AddTransient<NewProjectViewModel>();

        // Register Views as Transient (each dialog/window instance is fresh)
        _ = services.AddTransient<ProviderSettingsWindow>();
        _ = services.AddTransient<ImportProjectDialog>();
        _ = services.AddTransient<PreTranslateWindow>();

        // Register remaining ViewModels (transient by default so each window/dialog gets a fresh instance)
        _ = services.AddTransient<LanguagePromptViewModel>();
        _ = services.AddTransient<LanguageSummaryItemViewModel>();
        _ = services.AddTransient<OptionsViewModel>();
        _ = services.AddTransient<PreTranslateViewModel>();
        _ = services.AddTransient<ProviderSettingsViewModel>();
        _ = services.AddTransient<StartScreenViewModel>();
        _ = services.AddTransient<TranslationItemViewModel>();
        _ = services.AddTransient<ImportProjectViewModel>();

        // Factory for creating TranslationItemViewModel with a TranslationItem param
        _ = services.AddTransient<System.Func<Toucan.Core.Models.TranslationItem, TranslationItemViewModel>>(sp => ti => ActivatorUtilities.CreateInstance<TranslationItemViewModel>(sp, ti));

        // Register StatusBarViewModel as a singleton so any part of the app that requests it
        // can share the same instance (status is app-global)
        _ = services.AddSingleton<StatusBarViewModel>();

        // Factories for viewmodels that require runtime parameters so code can resolve instances via DI
        _ = services.AddTransient<Func<string, LanguageGroupViewModel>>(sp => ns => ActivatorUtilities.CreateInstance<LanguageGroupViewModel>(sp, ns));
        _ = services.AddTransient<Func<System.Windows.Window, AboutViewModel>>(sp => wnd => ActivatorUtilities.CreateInstance<AboutViewModel>(sp, wnd));
        _ = services.AddTransient<Func<Toucan.Core.Models.Project, System.Action<string>, RecentProjectViewModel>>(sp => (proj, act) => ActivatorUtilities.CreateInstance<RecentProjectViewModel>(sp, proj, act));
        _ = services.AddTransient<Func<System.Collections.Generic.IEnumerable<Toucan.Core.Models.TranslationItem>, LanguagePromptViewModel>>(sp => list => ActivatorUtilities.CreateInstance<LanguagePromptViewModel>(sp, list));
        // Factory for PreTranslateViewModel (languages + sourceItems + optional IPretranslationService)
        _ = services.AddTransient<Func<System.Collections.Generic.IEnumerable<string>, System.Collections.Generic.IEnumerable<Toucan.Core.Models.TranslationItem>, Toucan.Core.Contracts.Services.IPretranslationService, PreTranslateViewModel>>(
            sp => (langs, items, svc) => ActivatorUtilities.CreateInstance<PreTranslateViewModel>(sp, langs, items, svc));

        // Factory delegates for Views requiring runtime parameters
        _ = services.AddTransient<Func<IEnumerable<TranslationItem>, Window?, StatisticsDialog>>(sp => (translations, owner) => new StatisticsDialog(translations, owner));
        _ = services.AddTransient<Func<string, string, string, PromptDialog>>(sp => (title, message, defaultValue) => new PromptDialog(title, message, defaultValue));
        _ = services.AddTransient<Func<PreTranslateViewModel, PreTranslateWindow>>(sp => vm => new PreTranslateWindow(vm));

        _services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var viewModel = _services.GetRequiredService<MainWindowViewModel>();
        var statusBarViewModel = _services.GetRequiredService<StatusBarViewModel>();
        var mainWindow = new MainWindow(startupPath, viewModel, statusBarViewModel);

        // Register file association (per-user, no elevation needed)
        FileAssociationService.Register();

        mainWindow.Show();
    }
}
