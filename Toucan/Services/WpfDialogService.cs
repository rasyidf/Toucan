using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.ViewModels;
using Toucan.Views;
using Toucan.Views.Dialogs;

namespace Toucan.Services;

public class WpfDialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;

    public WpfDialogService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private static Window? Owner => Application.Current?.MainWindow;

    public string? SelectFolder(string? initialPath)
    {
        VistaFolderBrowserDialog dialog = new() { SelectedPath = initialPath ?? "" };
        return dialog.ShowDialog(Owner) == true ? dialog.SelectedPath : null;
    }

    public string? SelectFile(string? initialPath, string filter = "All Files (*.*)|*.*")
    {
        OpenFileDialog dialog = new()
        {
            InitialDirectory = initialPath ?? "",
            Filter = filter,
            Multiselect = false
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ShowPrompt(string title, string message, string defaultValue = "")
    {
        PromptDialog dialog = new(title, message, defaultValue) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.ResponseText : null;
    }

    public bool ShowAbout()
    {
        var factory = _serviceProvider.GetRequiredService<Func<Window, AboutViewModel>>();
        AboutDialog dialog = new(Owner!, factory);
        return dialog.ShowDialog() == true;
    }

    public bool ShowNewProject(IProjectService projectService, out NewProjectViewModel? resultVm)
    {
        resultVm = null;
        var vm = _serviceProvider.GetRequiredService<NewProjectViewModel>();
        NewProjectPrompt dialog = new(vm) { Owner = Owner };
        if (dialog.ShowDialog() == true)
        {
            resultVm = vm;
            return true;
        }
        return false;
    }

    public bool ShowOptions(AppOptions options, string currentPath, out AppOptions? updatedOptions)
    {
        updatedOptions = null;
        var vm = _serviceProvider.GetRequiredService<OptionsViewModel>();
        OptionDialog dialog = new(options, currentPath, vm) { Owner = Owner };
        if (dialog.ShowDialog() == true)
        {
            updatedOptions = dialog.Config;
            return true;
        }
        return false;
    }

    public bool ShowPreTranslate(PreTranslateViewModel vm)
    {
        PreTranslateWindow dialog = new(vm) { Owner = Owner };
        return dialog.ShowDialog() == true;
    }

    public bool ShowProviderSettings()
    {
        var vm = _serviceProvider.GetRequiredService<ProviderSettingsViewModel>();
        ProviderSettingsWindow dialog = new(vm) { Owner = Owner };
        _ = dialog.ShowDialog();
        return true;
    }

    public bool ShowProjectProperties(ProjectSettings settings, IEnumerable<string>? discoveredLanguages = null)
    {
        var vm = new ProjectPropertiesViewModel(settings, this, discoveredLanguages);
        ProjectPropertiesWindow dialog = new(vm) { Owner = Owner };
        return dialog.ShowDialog() == true;
    }

    public bool ShowImportProject(out ImportProjectViewModel? resultVm)
    {
        resultVm = null;
        var profiles = _serviceProvider.GetService(typeof(IEnumerable<IFrameworkProfile>)) as IEnumerable<IFrameworkProfile>;
        if (profiles == null)
        {
            return false;
        }

        ImportProjectViewModel vm = new(profiles, this);
        ImportProjectDialog dialog = new(vm) { Owner = Owner };
        if (dialog.ShowDialog() == true)
        {
            resultVm = vm;
            return true;
        }
        return false;
    }

    public string? ShowLanguagePrompt(string title, string message, IEnumerable<TranslationItem>? existingTranslations)
    {
        var factory = _serviceProvider.GetRequiredService<Func<IEnumerable<TranslationItem>, LanguagePromptViewModel>>();
        LanguagePrompt dialog = new(title, message, existingTranslations?.ToList(), factory) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.ResponseText : null;
    }

    public LanguageManagerViewModel? ShowManageLanguages(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null)
    {
        var vm = new LanguageManagerViewModel(allTranslations, primaryLanguage);
        ManageLanguagesDialog dialog = new(vm) { Owner = Owner };
        return dialog.ShowDialog() == true ? vm : null;
    }

    public void Shutdown()
    {
        Application.Current?.Shutdown();
    }
}
