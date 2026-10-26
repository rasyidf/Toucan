using Avalonia;
using Avalonia.Headless;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services;
using Toucan.Core.Services.Ai;

[assembly: AvaloniaTestApplication(typeof(Toucan.Avalonia.Tests.TestAppBuilder))]
// Windows, themes, command registries and side panels share one process-wide UI session.
// Concurrent collections can otherwise create or replace brushes on a worker thread.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

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
    /// <param name="root">Reuse another host's folders, to model a restart of the app on the same files.</param>
    public TestHost(string? root = null)
    {
        ResetProcessWideState();
        Root = root ?? Directory.CreateTempSubdirectory("toucan-tests-").FullName;
        Dialogs = new FakeDialogService();
        Messages = new FakeMessageService();
        Services = App.ConfigureServices(s =>
        {
            s.AddSingleton<IPreferenceService>(new InMemoryPreferences());
            s.AddSingleton<IRecentProjectService>(new RecentProjectService(Path.Combine(Root, "recent.json")));
            s.AddSingleton<ITranslationMemory>(new InMemoryTranslationMemory());
            s.AddSingleton<IProjectDefaultsService>(new ProjectDefaultsService(Path.Combine(Root, "settings")));
            // Recovery drafts must never land in the developer's Documents folder.
            s.AddSingleton(new DictionaryRegistry(Path.Combine(Root, "dictionaries")));
            s.AddSingleton<IRecoveryDraftService>(new RecoveryDraftService(Path.Combine(Root, "recovery")));
            s.AddSingleton<IDialogService>(Dialogs);
            s.AddSingleton<IAsyncMessageService>(Messages);
            s.AddSingleton<IMessageService>(Messages);
            // AI settings, secrets and prompts live in the temp folder: tests never read the user's keys or run the migration on their files.
            var protector = new SecureStorageService(Path.Combine(Root, "secret.key"));
            var secrets = new SecretService(protector, Path.Combine(Root, "secrets.json"));
            s.AddSingleton<ISecureStorageService>(protector);
            s.AddSingleton<ISecretService>(secrets);
            s.AddSingleton<IAiSettingsStore>(new AiSettingsStore(Path.Combine(Root, "ai.json")));
            s.AddSingleton<IPromptLibrary>(sp => new PromptLibrary(sp.GetServices<AiFeatureDefinition>(), Path.Combine(Root, "prompts")));
            s.AddSingleton<IProviderSettingsService>(new ProviderSettingsService(secrets, protector, Path.Combine(Root, "providers.json")));
        },
        // Never read or write the real Documents/Toucan plugin folder or trust file from tests.
        pluginRoot: Path.Combine(Root, "plugins"),
        pluginPolicyPath: Path.Combine(Root, "plugin-policy.json"),
        pluginDataRoot: Path.Combine(Root, "plugin-data"));

        var lifecycle = (ProjectLifecycleService)Services.GetRequiredService<IProjectLifecycleService>();
        lifecycle.SetUnsavedChangesHandler(new UnsavedChangesHandler(Messages));
        lifecycle.SetExternalChangeHandler(new ExternalChangeHandler(Messages));
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

    /// <summary>
    /// PanelService is a process-wide singleton and every new MainWindowViewModel starts in its editor mode,
    /// so a test that ends in Audit/Review mode (or Zen) silently turns the next test's text boxes read-only
    /// and makes its edit commands no-ops. Start and finish every test from the default layout.
    /// </summary>
    private static void ResetProcessWideState()
    {
        var panels = PanelService.Instance;
        if (panels.ZenMode) panels.ToggleZenMode();
        panels.EditorMode = EditorMode.Editor;
        panels.StatusBarVisible = true;
    }

    public void Dispose()
    {
        Services.Dispose();
        ResetProcessWideState();
        try { Directory.Delete(Root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
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
    public Queue<string?> Files { get; } = new();
    public Queue<string?> SaveFiles { get; } = new();

    public Task<string?> SelectFolderAsync(string? initialPath, string title = "Select Folder") => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);
    public Task<string?> SelectFileAsync(string? initialPath, string title = "Open File", IReadOnlyList<FileFilter>? filters = null) => Task.FromResult(Files.Count > 0 ? Files.Dequeue() : null);
    public Task<string?> SaveFileAsync(string? initialPath, string suggestedName, string title = "Save As", IReadOnlyList<FileFilter>? filters = null) => Task.FromResult(SaveFiles.Count > 0 ? SaveFiles.Dequeue() : null);
    public Task<string?> ShowPromptAsync(string title, string message, string defaultValue = "") => Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);
    public Task<string?> ShowPickAsync(string title, string message, IReadOnlyList<string> options, string? selected = null) => Task.FromResult(Picks.Count > 0 ? Picks.Dequeue() : null);
    public Task<string?> ShowLanguagePromptAsync(string title, string message, IEnumerable<TranslationItem>? existingTranslations) => Task.FromResult(Prompts.Count > 0 ? Prompts.Dequeue() : null);
    public string? RequestedFramework { get; private set; }
    public Task<NewProjectViewModel?> ShowNewProjectAsync(string? frameworkName = null)
    {
        RequestedFramework = frameworkName;
        return Task.FromResult<NewProjectViewModel?>(null);
    }
    public ImportProjectViewModel? ImportProjectResult { get; set; }
    public Task<ImportProjectViewModel?> ShowImportProjectAsync() => Task.FromResult(ImportProjectResult);
    public Task<AppOptions?> ShowOptionsAsync(int startPage = 0) => Task.FromResult<AppOptions?>(null);
    /// <summary>Lets a test act as the user inside the Pre-translate dialog.</summary>
    public Func<Toucan.Core.Services.SpreadsheetPreview, bool>? OnSpreadsheetPreview { get; set; }
    public Toucan.Core.Services.SpreadsheetPreview? LastSpreadsheetPreview { get; private set; }
    public Task<bool> ShowSpreadsheetPreviewAsync(Toucan.Core.Services.SpreadsheetPreview preview)
    {
        LastSpreadsheetPreview = preview;
        return Task.FromResult(OnSpreadsheetPreview?.Invoke(preview) ?? true);
    }
    public Action<PreTranslateViewModel>? OnPreTranslate { get; set; }
    public Task<bool> ShowPreTranslateAsync(PreTranslateViewModel vm)
    {
        OnPreTranslate?.Invoke(vm);
        return Task.FromResult(vm.AppliedCount > 0);
    }
    public Task ShowProviderSettingsAsync(string? projectPath = null) => Task.CompletedTask;
    /// <summary>Lets a test act as the user inside the prompt editor.</summary>
    public Action<PromptEditorViewModel>? OnPromptEditor { get; set; }
    public Task<bool> ShowPromptEditorAsync(PromptEditorViewModel vm)
    {
        OnPromptEditor?.Invoke(vm);
        return Task.FromResult(OnPromptEditor != null);
    }
    public int OnboardingShown { get; private set; }
    public Task ShowOnboardingAsync()
    {
        OnboardingShown++;
        return Task.CompletedTask;
    }
    public bool AcceptMigration { get; set; } = true;
    public Func<MigrationReport, bool>? OnMigrationReport { get; set; }
    public Task<bool> ShowMigrationReportAsync(MigrationReport report, bool canApply) => Task.FromResult(OnMigrationReport?.Invoke(report) ?? AcceptMigration);
    public Func<ProjectSettings, ProjectPropertiesViewModel?>? PropertiesHandler { get; set; }
    public Task<ProjectPropertiesViewModel?> ShowProjectPropertiesAsync(ProjectSettings settings, IEnumerable<string>? discoveredLanguages = null) => Task.FromResult(PropertiesHandler?.Invoke(settings));
    public Task<LanguageManagerViewModel?> ShowManageLanguagesAsync(IEnumerable<TranslationItem> allTranslations, string? primaryLanguage = null) => Task.FromResult<LanguageManagerViewModel?>(null);
    public Task ShowStatisticsAsync(IEnumerable<TranslationItem> translations) => Task.CompletedTask;
    public void Shutdown() { }
}
