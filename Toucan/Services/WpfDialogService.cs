using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Ookii.Dialogs.Wpf;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.ViewModels;
using Toucan.Views;
using Toucan.Views.Dialogs;

namespace Toucan.Services;

public class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public string? SelectFolder(string? initialPath)
    {
        var dialog = new VistaFolderBrowserDialog { SelectedPath = initialPath ?? "" };
        return dialog.ShowDialog(Owner) == true ? dialog.SelectedPath : null;
    }

    public string? SelectFile(string? initialPath, string filter = "All Files (*.*)|*.*")
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = initialPath ?? "",
            Filter = filter,
            Multiselect = false
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ShowPrompt(string title, string message, string defaultValue = "")
    {
        var dialog = new PromptDialog(title, message, defaultValue) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.ResponseText : null;
    }

    public bool ShowAbout()
    {
        var dialog = new AboutDialog(Owner!);
        return dialog.ShowDialog() == true;
    }

    public bool ShowNewProject(IProjectService projectService, out NewProjectViewModel? resultVm)
    {
        resultVm = null;
        var vm = App.Services?.GetService(typeof(NewProjectViewModel)) as NewProjectViewModel
            ?? new NewProjectViewModel(projectService);
        var dialog = new NewProjectPrompt(vm) { Owner = Owner };
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
        var dialog = new OptionDialog(options, currentPath) { Owner = Owner };
        if (dialog.ShowDialog() == true)
        {
            updatedOptions = dialog.Config;
            return true;
        }
        return false;
    }

    public bool ShowPreTranslate(PreTranslateViewModel vm)
    {
        var dialog = new PreTranslateWindow(vm) { Owner = Owner };
        return dialog.ShowDialog() == true;
    }

    public bool ShowProviderSettings()
    {
        var dialog = new ProviderSettingsWindow() { Owner = Owner };
        dialog.ShowDialog();
        return true;
    }

    public bool ShowImportProject(out ImportProjectViewModel? resultVm)
    {
        resultVm = null;
        var profiles = App.Services?.GetService(typeof(IEnumerable<Toucan.Core.Contracts.IFrameworkProfile>)) as IEnumerable<Toucan.Core.Contracts.IFrameworkProfile>;
        if (profiles == null) return false;
        var vm = new ImportProjectViewModel(profiles, this);
        var dialog = new ImportProjectDialog(vm) { Owner = Owner };
        if (dialog.ShowDialog() == true)
        {
            resultVm = vm;
            return true;
        }
        return false;
    }

    public string? ShowLanguagePrompt(string title, string message, IEnumerable<TranslationItem>? existingTranslations)
    {
        var dialog = new LanguagePrompt(title, message, existingTranslations?.ToList()) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.ResponseText : null;
    }

    public void Shutdown()
    {
        Application.Current?.Shutdown();
    }
}
