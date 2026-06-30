using System.Collections.Generic;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.ViewModels;

namespace Toucan.Services;

public interface IDialogService
{
    string? SelectFolder(string? initialPath);
    string? SelectFile(string? initialPath, string filter = "All Files (*.*)|*.*");

    // Generic prompt (returns user input or null if cancelled)
    string? ShowPrompt(string title, string message, string defaultValue = "");

    // Typed dialogs (return true if user confirmed/saved)
    bool ShowAbout();
    bool ShowNewProject(IProjectService projectService, out NewProjectViewModel? resultVm);
    bool ShowOptions(AppOptions options, string currentPath, out AppOptions? updatedOptions);
    bool ShowPreTranslate(PreTranslateViewModel vm);
    bool ShowProviderSettings();
    bool ShowProjectProperties(ProjectSettings settings, IEnumerable<string>? discoveredLanguages = null);
    bool ShowImportProject(out ImportProjectViewModel? resultVm);
    string? ShowLanguagePrompt(string title, string message, IEnumerable<TranslationItem>? existingTranslations);

    // Language management dialog (returns ViewModel with changes or null if cancelled)
    LanguageManagerViewModel? ShowManageLanguages(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null);

    // Application lifecycle
    void Shutdown();
}
