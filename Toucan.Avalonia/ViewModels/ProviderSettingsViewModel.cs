using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Services.Ai;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// Edits translation provider configuration (endpoints, models, API keys) either app-wide
/// or for a single project. Built-in providers always appear with their schema fields.
/// </summary>
public partial class ProviderSettingsViewModel : ObservableObject
{
    private readonly IProviderSettingsService _service;
    private readonly IDialogService _dialogs;
    private readonly ITranslationProviderRegistry _registry;

    public ProviderSettingsViewModel(IProviderSettingsService service, IDialogService dialogs, ITranslationProviderRegistry registry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        foreach (var def in _registry.GetAll().Where(d => !IsAiManaged(d.Name))) AvailableDefinitions.Add(def);
        LoadAppSettings();
    }

    public ObservableCollection<ProviderSettings> Providers { get; } = [];
    public ObservableCollection<KeyValueItem> OptionItems { get; } = [];
    public ObservableCollection<KeyValueItem> SecretItems { get; } = [];
    public ObservableCollection<ProviderDefinition> AvailableDefinitions { get; } = [];

    [ObservableProperty] private ProviderSettings? selected;
    [ObservableProperty] private ProviderDefinition? selectedDefinition;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeLabel))]
    private bool projectScope;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeLabel))]
    private string projectPath = string.Empty;

    [ObservableProperty] private string statusMessage = string.Empty;

    public string ScopeLabel => ProjectScope
        ? $"Project: {Path.GetFileName(ProjectPath)}"
        : "App-wide (all projects)";

    public event EventHandler? CloseRequested;

    /// <summary>Opens the dialog in project scope when a project is loaded.</summary>
    public void UseProject(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        ProjectPath = path;
        LoadProjectSettingsFrom(path);
    }

    partial void OnSelectedChanged(ProviderSettings? oldValue, ProviderSettings? newValue)
    {
        if (oldValue != null) FlushFieldsTo(oldValue);
        RebuildFieldItems();
    }

    [RelayCommand]
    private void LoadAppSettings()
    {
        MergeWithRegistry(_service.LoadAppProviderSettings().ToList());
        ProjectScope = false;
    }

    [RelayCommand]
    private async Task LoadProjectSettings()
    {
        var path = ProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = await _dialogs.SelectFolderAsync(null, "Project Folder") ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(path)) return;
        ProjectPath = path;
        LoadProjectSettingsFrom(path);
    }

    private void LoadProjectSettingsFrom(string path)
    {
        MergeWithRegistry(_service.LoadProjectProviderSettings(path).ToList());
        ProjectScope = true;
    }

    [RelayCommand]
    private void Save()
    {
        if (Selected != null) FlushFieldsTo(Selected);

        if (ProjectScope)
        {
            if (string.IsNullOrWhiteSpace(ProjectPath)) return;
            _service.SaveProjectProviderSettings(ProjectPath, Providers);
        }
        else
        {
            _service.SaveAppProviderSettings(Providers);
        }
        StatusMessage = $"Saved {DateTime.Now:t}";
    }

    [RelayCommand]
    private void SaveAndClose()
    {
        Save();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AddProvider(ProviderDefinition? definition)
    {
        var def = definition ?? _registry.GetByName("Custom");
        var newP = new ProviderSettings
        {
            Provider = def?.Name ?? "Custom",
            Options = def?.OptionFields.ToDictionary(f => f.Key, f => def.DefaultValues.GetValueOrDefault(f.Key, string.Empty)) ?? [],
            Secrets = def?.SecretFields.ToDictionary(f => f.Key, _ => string.Empty) ?? []
        };
        Providers.Add(newP);
        Selected = newP;
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (Selected == null) return;
        Providers.Remove(Selected);
        Selected = Providers.FirstOrDefault();
    }

    [RelayCommand]
    private void AddOption() => OptionItems.Add(new KeyValueItem(string.Empty, string.Empty, "New option key"));

    [RelayCommand]
    private void RemoveOption(KeyValueItem? item)
    {
        if (item is { IsSchemaField: false }) OptionItems.Remove(item);
    }

    [RelayCommand]
    private void AddSecret() => SecretItems.Add(new KeyValueItem(string.Empty, string.Empty, "New secret key"));

    [RelayCommand]
    private void RemoveSecret(KeyValueItem? item)
    {
        if (item is { IsSchemaField: false }) SecretItems.Remove(item);
    }

    private void MergeWithRegistry(List<ProviderSettings> saved)
    {
        // Detach the selection first so pending field edits aren't flushed into a stale object.
        Selected = null;
        Providers.Clear();

        // The AI provider is configured under Settings → AI, and Claude, OpenAI and Gemini entries from older versions are AI
        // services now (moved there on first run), so neither is edited here.
        var builtIn = _registry.GetAll().Where(d => !IsAiManaged(d.Name)).ToList();
        saved = [.. saved.Where(s => !IsAiManaged(s.Provider))];
        foreach (var def in builtIn)
        {
            var existing = saved.FirstOrDefault(s => string.Equals(s.Provider, def.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                foreach (var key in def.OptionFields.Keys) existing.Options.TryAdd(key, def.DefaultValues.GetValueOrDefault(key, string.Empty));
                foreach (var key in def.SecretFields.Keys) existing.Secrets.TryAdd(key, string.Empty);
                Providers.Add(existing);
            }
            else
            {
                Providers.Add(new ProviderSettings
                {
                    Provider = def.Name,
                    Options = def.OptionFields.ToDictionary(f => f.Key, f => def.DefaultValues.GetValueOrDefault(f.Key, string.Empty)),
                    Secrets = def.SecretFields.ToDictionary(f => f.Key, _ => string.Empty)
                });
            }
        }

        var builtInNames = builtIn.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in saved.Where(s => !builtInNames.Contains(s.Provider))) Providers.Add(extra);

        Selected = Providers.FirstOrDefault();
    }

    private static bool IsAiManaged(string provider) =>
        string.Equals(provider, AiFeatureIds.TranslationProviderName, StringComparison.OrdinalIgnoreCase)
        || LegacyAiMigration.LegacyProviders.ContainsKey(provider);

    private void RebuildFieldItems()
    {
        OptionItems.Clear();
        SecretItems.Clear();
        if (Selected == null)
        {
            SelectedDefinition = null;
            return;
        }

        SelectedDefinition = _registry.GetByName(Selected.Provider);
        foreach (var kv in Selected.Options)
        {
            var hint = SelectedDefinition?.OptionFields.GetValueOrDefault(kv.Key) ?? string.Empty;
            OptionItems.Add(new KeyValueItem(kv.Key, kv.Value, hint, SelectedDefinition?.OptionFields.ContainsKey(kv.Key) == true));
        }
        foreach (var kv in Selected.Secrets)
        {
            var hint = SelectedDefinition?.SecretFields.GetValueOrDefault(kv.Key) ?? string.Empty;
            SecretItems.Add(new KeyValueItem(kv.Key, kv.Value, hint, SelectedDefinition?.SecretFields.ContainsKey(kv.Key) == true));
        }
    }

    private void FlushFieldsTo(ProviderSettings target)
    {
        target.Options.Clear();
        foreach (var item in OptionItems.Where(i => !string.IsNullOrWhiteSpace(i.Key))) target.Options[item.Key] = item.Value;
        target.Secrets.Clear();
        foreach (var item in SecretItems.Where(i => !string.IsNullOrWhiteSpace(i.Key))) target.Secrets[item.Key] = item.Value;
    }
}
