using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Services;
using Toucan.Core.Services.SaveStrategies;
using Toucan.Core.Services.LoadStrategies;
using System;

namespace Toucan.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Core services
        services.AddLogging(b => b.AddDebug());
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<ITranslationStrategyFactory, TranslationStrategyFactory>();
        services.AddSingleton<IProjectModeResolver, ProjectModeResolver>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IBulkActionService, BulkActionService>();
        services.AddSingleton<IPretranslationService, PretranslationService>();

        // Translation providers
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.MockTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.DeepLTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.GoogleTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.MicrosoftTranslationProvider>();
        services.AddSingleton<ITranslationProvider, Core.Services.Providers.OpenAITranslationProvider>();

        // Save/Load strategies
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

        // Avalonia services
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IPreferenceService, PreferenceService>();
        services.AddSingleton<IRecentProjectService, RecentProjectService>();
        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<IProviderSettingsService, ProviderSettingsService>();

        // ViewModels
        services.AddSingleton<StatusBarViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<NewProjectViewModel>();
        services.AddTransient<OptionsViewModel>();
        services.AddTransient<PreTranslateViewModel>();
        services.AddTransient<ProviderSettingsViewModel>();
        services.AddTransient<StartScreenViewModel>();

        // Factories
        services.AddTransient<Func<string, LanguageGroupViewModel>>(sp => ns =>
            new LanguageGroupViewModel(ns));
        services.AddTransient<Func<System.Collections.Generic.IEnumerable<string>, System.Collections.Generic.IEnumerable<Toucan.Core.Models.TranslationItem>, IPretranslationService, PreTranslateViewModel>>(
            sp => (langs, items, svc) => new PreTranslateViewModel(langs, items, svc));

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            var statusVm = Services.GetRequiredService<StatusBarViewModel>();
            desktop.MainWindow = new MainWindow(vm, statusVm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static int Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
