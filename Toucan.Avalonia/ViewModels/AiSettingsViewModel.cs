using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Toucan.Core.Services.Ai;

namespace Toucan.Avalonia.ViewModels;

/// <summary>An AI service offered in Settings → AI.</summary>
public sealed record AiBackendOption(string Id, string DisplayName, string Description)
{
    public override string ToString() => DisplayName;
}

/// <summary>One AI feature in Settings → AI: its switch, model override and prompt.</summary>
public partial class AiFeatureItemViewModel(AiFeatureDefinition definition, AiFeatureSettings settings, AiSettingsViewModel owner) : ObservableObject
{
    public AiFeatureDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string Description => Definition.Description;

    [ObservableProperty] private bool enabled = settings.Enabled;
    [ObservableProperty] private string model = settings.Model ?? string.Empty;

    /// <summary>"Built-in prompt", "Your prompt" or "Project prompt": which version is in effect.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsCustomized))] private PromptSource promptSource;

    public bool IsCustomized => PromptSource != PromptSource.BuiltIn;
    public string PromptSourceText => PromptSource switch
    {
        PromptSource.Project => "Project prompt",
        PromptSource.User => "Your prompt",
        _ => "Built-in prompt",
    };

    partial void OnPromptSourceChanged(PromptSource value) => OnPropertyChanged(nameof(PromptSourceText));

    [RelayCommand]
    private Task EditPrompt() => owner.EditPromptAsync(this);
}

/// <summary>
/// Settings → AI: the app-wide switch, the AI service with its endpoint, model and API key, and the features with their
/// prompts. Changes are kept here until <see cref="Save"/>, which the Settings dialog calls on Save.
/// </summary>
public partial class AiSettingsViewModel : ObservableObject
{
    private readonly IAiSettingsStore _store;
    private readonly ISecretService _secrets;
    private readonly AiService _ai;
    private readonly IPromptLibrary _prompts;
    private readonly IDialogService _dialogs;
    private readonly Func<string?> _projectPath;
    private readonly AiSettings _settings;

    /// <summary>Endpoint, model and key typed per service, so switching services in the picker loses nothing.</summary>
    private readonly Dictionary<string, (string Endpoint, string Model, string ApiKey)> _drafts = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadingBackend;

    public AiSettingsViewModel(IAiSettingsStore store, ISecretService secrets, AiService ai, IPromptLibrary prompts, IDialogService dialogs, Func<string?>? projectPath = null)
    {
        _store = store;
        _secrets = secrets;
        _ai = ai;
        _prompts = prompts;
        _dialogs = dialogs;
        _projectPath = projectPath ?? (() => null);
        _settings = store.Load();

        foreach (var def in ai.Backends) Backends.Add(new AiBackendOption(def.Id, def.DisplayName, def.Description));
        foreach (var feature in prompts.Features)
            Features.Add(new AiFeatureItemViewModel(feature, _settings.FeatureSettings(feature.Id), this) { PromptSource = prompts.GetPrompt(feature.Id, ProjectPath).Source });

        enabled = _settings.Enabled;
        selectedBackend = Backends.FirstOrDefault(b => string.Equals(b.Id, _settings.Backend, StringComparison.OrdinalIgnoreCase)) ?? Backends.FirstOrDefault();
        LoadBackend(selectedBackend);
    }

    public ObservableCollection<AiBackendOption> Backends { get; } = [];
    public ObservableCollection<AiFeatureItemViewModel> Features { get; } = [];

    public string? ProjectPath => _projectPath() is { Length: > 0 } p ? p : null;
    public string PromptsFolder => _prompts.UserFolder;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StatusText))] private bool enabled;
    [ObservableProperty] private AiBackendOption? selectedBackend;
    [ObservableProperty] private string endpoint = string.Empty;
    [ObservableProperty] private string model = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(StatusText))] private string apiKey = string.Empty;
    [ObservableProperty] private string endpointPlaceholder = string.Empty;
    [ObservableProperty] private string modelPlaceholder = string.Empty;
    [ObservableProperty] private string apiKeyHint = string.Empty;
    [ObservableProperty] private string backendDescription = string.Empty;

    /// <summary>Result of the last connection test.</summary>
    [ObservableProperty] private string testResult = string.Empty;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))] private bool isTesting;

    public string StatusText => !Enabled
        ? "AI is off. Toucan sends nothing to an AI service, and AI commands are turned off."
        : RequiresKey && string.IsNullOrWhiteSpace(ApiKey) && string.IsNullOrEmpty(EnvironmentKey)
            ? "Add an API key to use AI."
            : "AI is on. Text is sent to the service below only when you run an AI command.";

    private bool RequiresKey => SelectedBackend is { } b && _ai.Find(b.Id)?.Definition.RequiresApiKey == true;

    private string? EnvironmentKey => SelectedBackend is { } b && _ai.Find(b.Id)?.Definition.ApiKeyEnvironmentVariable is { } env
        ? Environment.GetEnvironmentVariable(env)
        : null;

    partial void OnSelectedBackendChanged(AiBackendOption? oldValue, AiBackendOption? newValue)
    {
        if (_loadingBackend) return;
        if (oldValue != null) _drafts[oldValue.Id] = (Endpoint, Model, ApiKey);
        LoadBackend(newValue);
        TestResult = string.Empty;
        OnPropertyChanged(nameof(StatusText));
    }

    private void LoadBackend(AiBackendOption? option)
    {
        _loadingBackend = true;
        try
        {
            var def = option == null ? null : _ai.Find(option.Id)?.Definition;
            if (def == null)
            {
                Endpoint = Model = ApiKey = EndpointPlaceholder = ModelPlaceholder = ApiKeyHint = BackendDescription = string.Empty;
                return;
            }

            if (!_drafts.TryGetValue(def.Id, out var draft))
            {
                var saved = _settings.BackendSettings(def.Id);
                draft = (saved.Endpoint ?? string.Empty, saved.Model ?? string.Empty, _secrets.GetSecret(SecretKeys.Ai(def.Id)) ?? string.Empty);
            }
            (Endpoint, Model, ApiKey) = draft;
            EndpointPlaceholder = def.DefaultEndpoint;
            ModelPlaceholder = def.DefaultModel;
            BackendDescription = def.Description;
            ApiKeyHint = def.ApiKeyEnvironmentVariable is { } env
                ? $"Stored encrypted on this computer. If empty, {env} is used{(def.RequiresApiKey ? "" : "; local servers need no key")}."
                : "Stored encrypted on this computer.";
        }
        finally
        {
            _loadingBackend = false;
        }
    }

    /// <summary>The settings as edited, including the drafts of services not currently shown.</summary>
    private AiSettings Collect()
    {
        if (SelectedBackend != null) _drafts[SelectedBackend.Id] = (Endpoint, Model, ApiKey);
        var settings = _settings.Clone();
        settings.Enabled = Enabled;
        if (SelectedBackend != null) settings.Backend = SelectedBackend.Id;
        foreach (var (id, draft) in _drafts)
            settings.Backends[id] = new AiBackendSettings { Endpoint = Blank(draft.Endpoint), Model = Blank(draft.Model) };
        foreach (var f in Features)
            settings.Features[f.Id] = new AiFeatureSettings { Enabled = f.Enabled, Model = Blank(f.Model) };
        return settings;
    }

    /// <summary>Writes the settings and the API keys typed for each service. Called by the Settings dialog's Save.</summary>
    public void Save()
    {
        var settings = Collect();
        foreach (var (id, draft) in _drafts) _secrets.SetSecret(SecretKeys.Ai(id), Blank(draft.ApiKey));
        _store.Save(settings);
    }

    private bool CanTest() => !IsTesting && SelectedBackend != null;

    /// <summary>Sends a tiny request with the values as typed (not yet saved) and reports what came back.</summary>
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestConnection()
    {
        if (SelectedBackend == null || _ai.Find(SelectedBackend.Id) is not { } backend) return;
        var def = backend.Definition;
        var key = Blank(ApiKey) ?? EnvironmentKey;
        if (def.RequiresApiKey && string.IsNullOrWhiteSpace(key))
        {
            TestResult = "Enter an API key first.";
            return;
        }

        IsTesting = true;
        TestResult = "Testing…";
        try
        {
            var endpoint = new AiEndpoint(Blank(Endpoint) ?? def.DefaultEndpoint, Blank(Model) ?? def.DefaultModel, key);
            var reply = await Task.Run(() => backend.CompleteAsync(new AiCompletionRequest
            {
                System = "You are a connection test. Reply with the single word OK.",
                User = "Test",
                MaxTokens = 16,
                Temperature = 0,
            }, endpoint));
            TestResult = $"Connected to {endpoint.Model}. Reply: {Shorten(reply)}";
        }
        catch (AiRequestException ex)
        {
            TestResult = $"Failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void OpenPromptsFolder()
    {
        Directory.CreateDirectory(_prompts.UserFolder);
        PlatformService.RevealInFileManager(_prompts.UserFolder);
    }

    internal async Task EditPromptAsync(AiFeatureItemViewModel item)
    {
        var editor = new PromptEditorViewModel(_prompts, item.Definition, ProjectPath);
        await _dialogs.ShowPromptEditorAsync(editor);
        item.PromptSource = _prompts.GetPrompt(item.Id, ProjectPath).Source;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Shorten(string text)
    {
        var line = text.Trim().ReplaceLineEndings(" ");
        return line.Length > 60 ? line[..60] + "…" : line;
    }
}
