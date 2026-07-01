using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Services;

namespace Toucan.ViewModels;

public partial class ProviderSettingsViewModel : ObservableObject
{
    private readonly IProviderSettingsService _service;
    private readonly ISecureStorageService _secure;
    private readonly IDialogService _dialogs;
    private readonly ITranslationProviderRegistry _registry;

    public ObservableCollection<ProviderSettings> Providers { get; } = [];

    [ObservableProperty]
    private ProviderSettings? selected;

    [ObservableProperty]
    private bool projectScope;

    [ObservableProperty]
    private string projectPath = string.Empty;

    /// <summary>The definition for the currently selected provider (null if unknown/custom).</summary>
    [ObservableProperty]
    private ProviderDefinition? selectedDefinition;

    /// <summary>Editable option entries for the selected provider.</summary>
    public ObservableCollection<KeyValueItem> OptionItems { get; } = [];

    /// <summary>Editable secret entries for the selected provider.</summary>
    public ObservableCollection<KeyValueItem> SecretItems { get; } = [];

    /// <summary>Available built-in provider names for the "Add Provider" combo.</summary>
    public ObservableCollection<ProviderDefinition> AvailableDefinitions { get; } = [];

    public ProviderSettingsViewModel(IProviderSettingsService service, ISecureStorageService secure, IDialogService dialogs, ITranslationProviderRegistry registry)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _secure = secure ?? throw new ArgumentNullException(nameof(secure));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        foreach (var def in _registry.GetAll())
        {
            AvailableDefinitions.Add(def);
        }

        LoadAppSettings();
    }

    partial void OnSelectedChanged(ProviderSettings? oldValue, ProviderSettings? newValue)
    {
        RebuildFieldItems(oldValue);
    }

    [RelayCommand]
    private void LoadAppSettings()
    {
        var saved = _service.LoadAppProviderSettings().ToList();
        MergeWithRegistry(saved);
        ProjectScope = false;
    }

    [RelayCommand]
    private void LoadProjectSettings()
    {
        var path = ProjectPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var folder = _dialogs.SelectFolder(Environment.CurrentDirectory);
            if (!string.IsNullOrEmpty(folder))
            {
                path = folder;
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var saved = _service.LoadProjectProviderSettings(path).ToList();
        MergeWithRegistry(saved);
        ProjectScope = true;
        ProjectPath = path;
    }

    [RelayCommand]
    private void Save()
    {
        // Flush current field edits back into the Selected provider's dictionaries
        FlushFieldsToSelected();

        if (ProjectScope)
        {
            if (string.IsNullOrWhiteSpace(ProjectPath))
            {
                return;
            }

            _service.SaveProjectProviderSettings(ProjectPath, Providers);
        }
        else
        {
            _service.SaveAppProviderSettings(Providers);
        }
    }

    [RelayCommand]
    private void AddProvider(ProviderDefinition? definition)
    {
        // If a definition is given, add that; otherwise add a blank custom entry
        var def = definition ?? _registry.GetByName("Custom");

        var newP = new ProviderSettings
        {
            Provider = def?.Name ?? "Custom",
            Options = new Dictionary<string, string>(def?.OptionFields.ToDictionary(f => f.Key, _ => string.Empty) ?? []),
            Secrets = new Dictionary<string, string>(def?.SecretFields.ToDictionary(f => f.Key, _ => string.Empty) ?? [])
        };

        Providers.Add(newP);
        Selected = newP;
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (Selected == null)
        {
            return;
        }

        // Built-in providers can be removed from the list (they'll re-appear on next load with empty values)
        _ = Providers.Remove(Selected);
        Selected = Providers.FirstOrDefault();
    }

    [RelayCommand]
    private void SelectProjectFolder()
    {
        var folder = _dialogs.SelectFolder(ProjectPath ?? Environment.CurrentDirectory);
        if (!string.IsNullOrEmpty(folder))
        {
            ProjectPath = folder;
        }
    }

    [RelayCommand]
    private void AddOption()
    {
        OptionItems.Add(new KeyValueItem("", "", "New option key", isSchemaField: false));
    }

    [RelayCommand]
    private void RemoveOption(KeyValueItem? item)
    {
        if (item != null && !item.IsSchemaField)
        {
            OptionItems.Remove(item);
        }
    }

    [RelayCommand]
    private void AddSecret()
    {
        SecretItems.Add(new KeyValueItem("", "", "New secret key", isSchemaField: false));
    }

    [RelayCommand]
    private void RemoveSecret(KeyValueItem? item)
    {
        if (item != null && !item.IsSchemaField)
        {
            SecretItems.Remove(item);
        }
    }

    /// <summary>
    /// Merges saved provider settings with the registry definitions, ensuring all
    /// built-in providers appear in the list with their required fields pre-populated.
    /// </summary>
    private void MergeWithRegistry(List<ProviderSettings> saved)
    {
        // Flush any pending edits before replacing the collection
        FlushFieldsToSelected();

        Providers.Clear();

        var builtInDefs = _registry.GetAll();

        // Add built-in providers (merging with saved values if present)
        foreach (var def in builtInDefs)
        {
            var existing = saved.FirstOrDefault(s => string.Equals(s.Provider, def.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                // Ensure all schema fields exist even if not saved
                foreach (var optKey in def.OptionFields.Keys)
                {
                    existing.Options.TryAdd(optKey, string.Empty);
                }
                foreach (var secKey in def.SecretFields.Keys)
                {
                    existing.Secrets.TryAdd(secKey, string.Empty);
                }
                Providers.Add(existing);
            }
            else
            {
                // Create a fresh entry with default values
                Providers.Add(new ProviderSettings
                {
                    Provider = def.Name,
                    Options = new Dictionary<string, string>(def.OptionFields.ToDictionary(f => f.Key, f => def.DefaultValues.GetValueOrDefault(f.Key, string.Empty))),
                    Secrets = new Dictionary<string, string>(def.SecretFields.ToDictionary(f => f.Key, _ => string.Empty))
                });
            }
        }

        // Add any saved providers that aren't built-in (user custom entries)
        var builtInNames = builtInDefs.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in saved.Where(s => !builtInNames.Contains(s.Provider)))
        {
            Providers.Add(extra);
        }

        Selected = Providers.FirstOrDefault();
    }

    /// <summary>
    /// Rebuilds the OptionItems and SecretItems collections from the currently selected provider.
    /// </summary>
    private void RebuildFieldItems(ProviderSettings? previousSelection = null)
    {
        // Flush previous selection's edits before switching
        if (previousSelection != null && OptionItems.Count > 0)
        {
            previousSelection.Options.Clear();
            foreach (var item in OptionItems.Where(i => !string.IsNullOrWhiteSpace(i.Key)))
                previousSelection.Options[item.Key] = item.Value;
            previousSelection.Secrets.Clear();
            foreach (var item in SecretItems.Where(i => !string.IsNullOrWhiteSpace(i.Key)))
                previousSelection.Secrets[item.Key] = item.Value;
        }

        OptionItems.Clear();
        SecretItems.Clear();

        if (Selected == null)
        {
            SelectedDefinition = null;
            return;
        }

        SelectedDefinition = _registry.GetByName(Selected.Provider);

        // Build option items
        foreach (var kv in Selected.Options)
        {
            var hint = SelectedDefinition?.OptionFields.GetValueOrDefault(kv.Key) ?? string.Empty;
            bool isSchema = SelectedDefinition?.OptionFields.ContainsKey(kv.Key) == true;
            OptionItems.Add(new KeyValueItem(kv.Key, kv.Value, hint, isSchema));
        }

        // Build secret items
        foreach (var kv in Selected.Secrets)
        {
            var hint = SelectedDefinition?.SecretFields.GetValueOrDefault(kv.Key) ?? string.Empty;
            bool isSchema = SelectedDefinition?.SecretFields.ContainsKey(kv.Key) == true;
            SecretItems.Add(new KeyValueItem(kv.Key, kv.Value, hint, isSchema));
        }
    }

    /// <summary>
    /// Writes the current OptionItems/SecretItems back into the Selected provider's dictionaries.
    /// Called before save or before switching selection.
    /// </summary>
    private void FlushFieldsToSelected()
    {
        if (Selected == null) return;

        Selected.Options.Clear();
        foreach (var item in OptionItems.Where(i => !string.IsNullOrWhiteSpace(i.Key)))
        {
            Selected.Options[item.Key] = item.Value;
        }

        Selected.Secrets.Clear();
        foreach (var item in SecretItems.Where(i => !string.IsNullOrWhiteSpace(i.Key)))
        {
            Selected.Secrets[item.Key] = item.Value;
        }
    }
}
