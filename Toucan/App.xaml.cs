using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Toucan.Services;
using Toucan.ViewModels;
using Toucan.Core.Services;
using Toucan.Core.Services.SaveStrategies;
using Toucan.Core.Contracts.Services;

namespace Toucan;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        string startupPath = "";

        if (e.Args.Length == 1)
        {
            startupPath = e.Args[0];
        }

        var recentService = new RecentProjectService();
        var dialogService = new DialogService();
        var messageService = new MessageService();
        var preferenceService = new PreferenceService();

        // Create project service and save strategies for the non-DI App (fallback)
        var fileService = new FileService();
        var saveStrategies = new List<ISaveStrategy>
        {
            new JsonSaveStrategy(fileService),
            new NamespacedSaveStrategy(fileService)
        };

        var projectService = new ProjectService(fileService, saveStrategies);

        var viewModel = new MainWindowViewModel(
            recentService,
            dialogService,
            messageService,
            preferenceService,
            null,
            projectService);

        var mainWindow = new MainWindow(startupPath, viewModel);
        mainWindow.Show();


    }
}
