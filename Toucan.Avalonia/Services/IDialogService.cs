using Toucan.Avalonia.ViewModels;
using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Avalonia.Services;

/// <summary>A file picker filter, e.g. <c>new FileFilter("JSON", "*.json")</c>.</summary>
public sealed class FileFilter(string name, params string[] patterns)
{
    public string Name { get; } = name;
    public IReadOnlyList<string> Patterns { get; } = patterns;
}

/// <summary>
/// Opens pickers and modal dialogs. All methods are asynchronous because Avalonia dialogs
/// are; they return null (or false) when the user cancels.
/// </summary>
public interface IDialogService
{
    Task<string?> SelectFolderAsync(string? initialPath, string title = "Select Folder");
    Task<string?> SelectFileAsync(string? initialPath, string title = "Open File", IReadOnlyList<FileFilter>? filters = null);
    Task<string?> SaveFileAsync(string? initialPath, string suggestedName, string title = "Save As", IReadOnlyList<FileFilter>? filters = null);

    Task<string?> ShowPromptAsync(string title, string message, string defaultValue = "");
    Task<string?> ShowPickAsync(string title, string message, IReadOnlyList<string> options, string? selected = null);
    Task<string?> ShowLanguagePromptAsync(string title, string message, IEnumerable<TranslationItem>? existingTranslations);

    Task<NewProjectViewModel?> ShowNewProjectAsync();
    Task<ImportProjectViewModel?> ShowImportProjectAsync();
    Task<AppOptions?> ShowOptionsAsync(int startPage = 0);
    Task<bool> ShowPreTranslateAsync(PreTranslateViewModel vm);
    Task ShowProviderSettingsAsync(string? projectPath = null);
    /// <summary>Edits one AI feature's prompt; true when it was saved or reset.</summary>
    Task<bool> ShowPromptEditorAsync(PromptEditorViewModel vm);
    /// <summary>First-run setup: asks whether to use AI features.</summary>
    Task ShowOnboardingAsync();
    Task<ProjectPropertiesViewModel?> ShowProjectPropertiesAsync(ProjectSettings settings, IEnumerable<string>? discoveredLanguages = null);
    Task<LanguageManagerViewModel?> ShowManageLanguagesAsync(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null);
    Task ShowStatisticsAsync(IEnumerable<TranslationItem> translations);

    void Shutdown();
}
