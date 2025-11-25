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
    public static IServiceProvider Services { get; private set; }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
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
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IPreferenceService, PreferenceService>();

        // core file service
        services.AddSingleton<IFileService, FileService>();

        // Bulk action service
        services.AddSingleton<IBulkActionService, Toucan.Core.Services.BulkActionService>();

        // Pretranslation engine and a simple mock provider
        services.AddSingleton<Toucan.Core.Contracts.Services.IPretranslationService, Toucan.Core.Services.PretranslationService>();
        services.AddSingleton<Toucan.Core.Contracts.ITranslationProvider, Toucan.Core.Services.Providers.MockTranslationProvider>();
        services.AddSingleton<Toucan.Core.Contracts.ITranslationProvider, Toucan.Core.Services.Providers.DeepLTranslationProvider>();
        services.AddSingleton<Toucan.Core.Contracts.ITranslationProvider, Toucan.Core.Services.Providers.GoogleTranslationProvider>();
        services.AddSingleton<Toucan.Core.Contracts.ITranslationProvider, Toucan.Core.Services.Providers.MicrosoftTranslationProvider>();
        services.AddSingleton<Toucan.Core.Contracts.ITranslationProvider, Toucan.Core.Services.Providers.OpenAITranslationProvider>();

        // Save Strategies - register concrete types and interface mappings
        services.AddSingleton<JsonSaveStrategy>();
        services.AddSingleton<NamespacedSaveStrategy>();
        services.AddSingleton<PoSaveStrategy>();
        services.AddSingleton<IniSaveStrategy>();
        services.AddSingleton<YamlSaveStrategy>();

        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<JsonSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<NamespacedSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<PoSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<IniSaveStrategy>());
        services.AddSingleton<ISaveStrategy>(sp => sp.GetRequiredService<YamlSaveStrategy>());

        // Load strategies - register concrete types and interface mappings
        services.AddSingleton<JsonLoadStrategy>();
        services.AddSingleton<NamespacedLoadStrategy>();
        services.AddSingleton<ManifestLoadStrategy>();
        services.AddSingleton<YamlLoadStrategy>();

        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<JsonLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<NamespacedLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<ManifestLoadStrategy>());
        services.AddSingleton<ILoadStrategy>(sp => sp.GetRequiredService<YamlLoadStrategy>());

        // Strategy factory and mode resolver
        services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();
        services.AddSingleton<IProjectModeResolver, ProjectModeResolver>();

        // Project service
        services.AddSingleton<IProjectService, ProjectService>();

        // Register MainWindowViewModel
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<NewProjectPrompt>();

        var serviceProvider = services.BuildServiceProvider();
        Services = serviceProvider;

        var viewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
        var mainWindow = new MainWindow(startupPath, viewModel);
        mainWindow.Show();


    }
}
