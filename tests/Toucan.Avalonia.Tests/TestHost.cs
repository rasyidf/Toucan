using Avalonia;
using Avalonia.Headless;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;

[assembly: AvaloniaTestApplication(typeof(Toucan.Avalonia.Tests.TestAppBuilder))]

namespace Toucan.Avalonia.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        // Never depend on the developer's saved UI language.
        Toucan.Avalonia.Locales.Loc.Use("en-US");
        return AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}

/// <summary>
/// The real service graph with test doubles for everything that touches the user's folders
/// (settings, recent projects, translation memory) or needs a human (dialogs, message boxes).
/// </summary>
internal sealed class TestHost : IDisposable
{
    public TestHost()
    {
        Root = Directory.CreateTempSubdirectory("toucan-tests-").FullName;
        Dialogs = new FakeDialogService();
        Messages = new FakeMessageService();
        Services = App.ConfigureServices(s =>
        {
            s.AddSingleton<IPreferenceService>(new InMemoryPreferences());
            s.AddSingleton<IRecentProjectService>(new RecentProjectService(Path.Combine(Root, "recent.json")));
            s.AddSingleton<ITranslationMemory>(new InMemoryTranslationMemory());
            s.AddSingleton<IDialogService>(Dialogs);
            s.AddSingleton<IAsyncMessageService>(Messages);
            s.AddSingleton<IMessageService>(Messages);
        },
        // Never read or write the real Documents/Toucan plugin folder or trust file from tests.
        pluginRoot: Path.Combine(Root, "plugins"),
        pluginPolicyPath: Path.Combine(Root, "plugin-policy.json"));
    }

    public string Root { get; }
    public ServiceProvider Services { get; }
    public FakeDialogService Dialogs { get; }
    public FakeMessageService Messages { get; }

    public MainWindowViewModel CreateViewModel() => Services.GetRequiredService<MainWindowViewModel>();

    /// <summary>Creates a flat-JSON project (one file per language) and returns its folder.</summary>
    public string CreateJsonProject(string name, params (string Language, string Json)[] files)
    {
        var folder = Path.Combine(Root, name);
        Directory.CreateDirectory(folder);
        foreach (var (lang, json) in files) File.WriteAllText(Path.Combine(folder, lang + ".json"), json);
        return folder;
    }

    public void Dispose()
    {
        Services.Dispose();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
    }
}

internal sealed class InMemoryPreferences : IPreferenceService
{
    private AppOptions _options = new() { OpenLastProjectOnStartup = false };
    public AppOptions Load() => _options;
    public void Save(AppOptions options) => _options = options;
}

internal sealed class InMemoryTranslationMemory : ITranslationMemory
{
    public List<TranslationMemoryEntry> Entries { get; } = [];
    public int Count => Entries.Count;
    public void Add(string sourceText, string targetText, string sourceLanguage, string targetLanguage) =>
        Entries.Add(new(sourceText, targetText, sourceLanguage, targetLanguage, DateTime.UtcNow));
    public void AddRange(IEnumerable<TranslationMemoryEntry> entries) => Entries.AddRange(entries);
    public IEnumerable<TranslationMemoryMatch> Search(string sourceText, string sourceLanguage, string targetLanguage, int maxResults = 5) =>
        Search(sourceText, sourceLanguage, targetLanguage, 0.5, maxResults);
    public IEnumerable<TranslationMemoryMatch> Search(string sourceText, string sourceLanguage, string targetLanguage, double minSimilarity, int maxResults = 5) =>
        Entries.Where(e => e.SourceText == sourceText && e.SourceLanguage == sourceLanguage && e.TargetLanguage == targetLanguage)
            .Select(e => new TranslationMemoryMatch(e.SourceText, e.TargetText, 1.0)).Take(maxResults);
    public IReadOnlyList<TmEntry> GetAllEntries() => Entries.Select(e => new TmEntry(e.SourceLanguage, e.SourceText, e.TargetLanguage, e.TargetText, e.Timestamp)).ToList();
    public void RemoveEntry(string sourceText, string targetLang) => Entries.RemoveAll(e => e.SourceText == sourceText && e.TargetLanguage == targetLang);
    public void Clear() => Entries.Clear();
}

/// <summary>Message boxes that answer from a script instead of waiting for a user.</summary>
internal sealed class FakeMessageService : IAsyncMessageService
{
    public List<string> Shown { get; } = [];
    public bool ConfirmAnswer { get; set; } = true;
    public ChoiceResult ChoiceAnswer { get; set; } = ChoiceResult.Primary;
    public List<string> Choices { get; } = [];

    public void ShowMessage(string message, string title = "Info") => Shown.Add(message);
    public bool ShowConfirmation(string message, string title = "Confirm") => ConfirmAnswer;
    public Task ShowMessageAsync(string message, string title = "Info")
    {
        Shown.Add(message);
        return Task.CompletedTask;
    }
    public Task<bool> ConfirmAsync(string message, string title = "Confirm", string yesText = "Yes", string noText = "No") => Task.FromResult(ConfirmAnswer);
    public Task<ChoiceResult> ChooseAsync(string message, string title, string primaryText, string secondaryText, string cancelText = "Cancel")
    {
        Choices.Add(message);
        return Task.FromResult(ChoiceAnswer);
    }
}

/// <summary>Dialogs that return queued answers.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public Queue<string?> Prompts { get; } = new();
    public Queue<string?> Picks { get; } = new();
    public Queue<string?> Folders { get; } = new();

    public Task<string?> SelectFolderAsync(string? initialPath, string title = "Select Folder") => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);
    public Task<string?> SelectFileAsync(string? initialPath, string title = "Open File", IReadOnlyList<FileFilter>? filters = null) => Task.FromResult<string?>(null);
    public Task<string?> SaveFileAsync(string? initialPath, string suggestedName, string title = "Save As", IReadOnlyList<FileFilter>? filters = null) => Task.FromResult<string?>(null);
    public Task<string?> ShowPromptAsync(string title, string message, string defaultValue = "") => Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);
    public Task<string?> ShowPickAsync(string title, string message, IReadOnlyList<string> options, string? selected = null) => Task.FromResult(Picks.Count > 0 ? Picks.Dequeue() : null);
    public Task<string?> ShowLanguagePromptAsync(string title, string message, IEnumerable<TranslationItem>? existingTranslations) => Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);
    public Task<NewProjectViewModel?> ShowNewProjectAsync() => Task.FromResult<NewProjectViewModel?>(null);
    public Task<ImportProjectViewModel?> ShowImportProjectAsync() => Task.FromResult<ImportProjectViewModel?>(null);
    public Task<AppOptions?> ShowOptionsAsync(int startPage = 0) => Task.FromResult<AppOptions?>(null);
    /// <summary>Lets a test act as the user inside the Pre-translate dialog.</summary>
    public Action<PreTranslateViewModel>? OnPreTranslate { get; set; }
    public Task<bool> ShowPreTranslateAsync(PreTranslateViewModel vm)
    {
        OnPreTranslate?.Invoke(vm);
        return Task.FromResult(false);
    }
    public Task ShowProviderSettingsAsync(string? projectPath = null) => Task.CompletedTask;
    public Task<ProjectPropertiesViewModel?> ShowProjectPropertiesAsync(ProjectSettings settings, IEnumerable<string>? discoveredLanguages = null) => Task.FromResult<ProjectPropertiesViewModel?>(null);
    public Task<LanguageManagerViewModel?> ShowManageLanguagesAsync(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null) => Task.FromResult<LanguageManagerViewModel?>(null);
    public Task ShowStatisticsAsync(IEnumerable<TranslationItem> translations) => Task.CompletedTask;
    public void Shutdown() { }
}
