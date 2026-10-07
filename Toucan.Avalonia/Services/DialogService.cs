using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Avalonia.Services;

/// <summary>Avalonia implementation of <see cref="IDialogService"/>: storage pickers and modal windows.</summary>
public sealed class DialogService(IServiceProvider services) : IDialogService
{
    private static Window Owner => AppWindows.Active ?? throw new InvalidOperationException("No window is open to own the dialog.");

    private static async Task<IStorageFolder?> StartFolderAsync(IStorageProvider storage, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        return string.IsNullOrEmpty(dir) || !Directory.Exists(dir) ? null : await storage.TryGetFolderFromPathAsync(dir);
    }

    private static List<FilePickerFileType>? ToFileTypes(IReadOnlyList<FileFilter>? filters) =>
        filters?.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns.ToList() }).ToList();

    public async Task<string?> SelectFolderAsync(string? initialPath, string title = "Select Folder")
    {
        var storage = Owner.StorageProvider;
        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await StartFolderAsync(storage, initialPath),
            AllowMultiple = false
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SelectFileAsync(string? initialPath, string title = "Open File", IReadOnlyList<FileFilter>? filters = null)
    {
        var storage = Owner.StorageProvider;
        var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await StartFolderAsync(storage, initialPath),
            AllowMultiple = false,
            FileTypeFilter = ToFileTypes(filters)
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string? initialPath, string suggestedName, string title = "Save As", IReadOnlyList<FileFilter>? filters = null)
    {
        var storage = Owner.StorageProvider;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = await StartFolderAsync(storage, initialPath),
            FileTypeChoices = ToFileTypes(filters),
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    public Task<string?> ShowPromptAsync(string title, string message, string defaultValue = "") =>
        new PromptDialog(title, message, defaultValue).ShowDialog<string?>(Owner);

    public Task<string?> ShowPickAsync(string title, string message, IReadOnlyList<string> options, string? selected = null) =>
        new PickDialog(title, message, options, selected).ShowDialog<string?>(Owner);

    public Task<string?> ShowLanguagePromptAsync(string title, string message, IEnumerable<TranslationItem>? existingTranslations)
    {
        var vm = new LanguagePromptViewModel(existingTranslations) { Title = title, Message = message };
        return new LanguagePromptDialog(vm).ShowDialog<string?>(Owner);
    }

    public async Task<NewProjectViewModel?> ShowNewProjectAsync()
    {
        var vm = services.GetRequiredService<NewProjectViewModel>();
        return await new NewProjectDialog(vm).ShowDialog<bool>(Owner) ? vm : null;
    }

    public async Task<ImportProjectViewModel?> ShowImportProjectAsync()
    {
        var vm = new ImportProjectViewModel(services.GetServices<IFrameworkProfile>(), this);
        return await new ImportProjectDialog(vm).ShowDialog<bool>(Owner) ? vm : null;
    }

    public async Task<AppOptions?> ShowOptionsAsync(int startPage = 0)
    {
        var vm = services.GetRequiredService<OptionsViewModel>();
        vm.SelectedPageIndex = Math.Clamp(startPage, 0, OptionsViewModel.Pages.Count - 1);
        return await new OptionsDialog(vm).ShowDialog<bool>(Owner) ? vm.AppOptions : null;
    }

    public async Task<bool> ShowPreTranslateAsync(PreTranslateViewModel vm) =>
        await new PreTranslateDialog(vm).ShowDialog<bool>(Owner);

    public async Task ShowProviderSettingsAsync(string? projectPath = null)
    {
        var vm = services.GetRequiredService<ProviderSettingsViewModel>();
        vm.UseProject(projectPath);
        await new ProviderSettingsDialog(vm).ShowDialog(Owner);
    }

    public async Task<bool> ShowPromptEditorAsync(PromptEditorViewModel vm) =>
        await new PromptEditorDialog(vm).ShowDialog<bool>(Owner);

    public async Task ShowOnboardingAsync()
    {
        var vm = services.GetRequiredService<OnboardingViewModel>();
        await new OnboardingDialog(vm).ShowDialog<bool>(Owner);
    }

    public async Task<ProjectPropertiesViewModel?> ShowProjectPropertiesAsync(ProjectSettings settings, IEnumerable<string>? discoveredLanguages = null)
    {
        var vm = new ProjectPropertiesViewModel(settings, this, discoveredLanguages, services.GetService<IProjectDefaultsService>());
        return await new ProjectPropertiesDialog(vm).ShowDialog<bool>(Owner) ? vm : null;
    }

    public async Task<LanguageManagerViewModel?> ShowManageLanguagesAsync(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null)
    {
        var vm = new LanguageManagerViewModel(allTranslations, primaryLanguage);
        return await new ManageLanguagesDialog(vm).ShowDialog<bool>(Owner) ? vm : null;
    }

    public Task ShowStatisticsAsync(IEnumerable<TranslationItem> translations) =>
        new StatisticsDialog(new StatisticsViewModel(translations)).ShowDialog(Owner);

    public void Shutdown()
    {
        // Closing the main window runs the unsaved-changes check before the app exits.
        AppWindows.Main?.Close();
    }
}
