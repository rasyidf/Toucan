using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<GlossaryEntry> GlossaryEntries { get; } = [];
    public ObservableCollection<GlossaryEntry> GlossaryMatches { get; } = [];
    [ObservableProperty] private GlossaryEntry? selectedGlossaryEntry;
    [ObservableProperty] private string glossaryTerm = "";
    [ObservableProperty] private string glossaryDescription = "";
    [ObservableProperty] private string glossaryLanguage = "";
    [ObservableProperty] private string glossaryTranslation = "";
    [ObservableProperty] private bool glossaryForbidden;

    partial void OnSelectedGlossaryEntryChanged(GlossaryEntry? value)
    {
        GlossaryTerm = value?.Term ?? ""; GlossaryDescription = value?.Description ?? "";
        GlossaryForbidden = value?.Forbidden ?? false;
        GlossaryTranslation = value?.Translations.GetValueOrDefault(GlossaryLanguage) ?? "";
    }
    partial void OnGlossaryLanguageChanged(string value) => GlossaryTranslation = SelectedGlossaryEntry?.Translations.GetValueOrDefault(value) ?? "";

    internal void RefreshGlossary()
    {
        GlossaryEntries.Clear(); GlossaryMatches.Clear();
        foreach (var entry in ProjectSettings?.Glossary ?? []) GlossaryEntries.Add(entry);
        var key = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        var source = key is null ? "" : ItemsForEditorKey(key).FirstOrDefault(i => i.Language == PrimaryLanguage)?.Value ?? "";
        foreach (var entry in GlossaryService.Matches(GlossaryEntries, source)) GlossaryMatches.Add(entry);
    }

    private void ChangeGlossary(Action<ProjectSettings> change)
    {
        if (ProjectSettings is not { } settings || IsAuditMode) return;
        FlushPendingEdits();
        var before = ProjectConfiguration.Copy(settings); var after = ProjectConfiguration.Copy(settings);
        change(after);
        _undoRedoService.RecordConfiguration(before, after);
        ProjectConfiguration.Apply(settings, after);
        _translationStore.NotifyConfigurationChanged(true); MarkStructureChanged(); RefreshGlossary();
        foreach (var row in PagingController.Data.SelectMany(g => g.AllItems)) row.RefreshPlaceholderWarning();
    }

    [RelayCommand]
    private void SaveGlossaryTerm()
    {
        if (string.IsNullOrWhiteSpace(GlossaryTerm)) return;
        var term = GlossaryTerm.Trim(); var previous = SelectedGlossaryEntry?.Term;
        if (GlossaryEntries.Any(e => e.Term.Equals(term, StringComparison.OrdinalIgnoreCase) && e.Term != previous))
        { StatusText = "Select the existing term to update it."; return; }
        ChangeGlossary(settings =>
        {
            var entry = settings.Glossary.FirstOrDefault(e => e.Term == previous);
            if (entry is null) settings.Glossary.Add(entry = new());
            entry.Term = term; entry.Description = GlossaryDescription; entry.Forbidden = GlossaryForbidden;
            if (!string.IsNullOrWhiteSpace(GlossaryLanguage))
            {
                if (string.IsNullOrWhiteSpace(GlossaryTranslation)) entry.Translations.Remove(GlossaryLanguage.Trim());
                else entry.Translations[GlossaryLanguage.Trim()] = GlossaryTranslation.Trim();
            }
        });
        SelectedGlossaryEntry = GlossaryEntries.FirstOrDefault(e => e.Term == term);
    }

    [RelayCommand] private void NewGlossaryTerm() { SelectedGlossaryEntry = null; GlossaryTerm = ""; GlossaryDescription = ""; GlossaryTranslation = ""; GlossaryForbidden = false; }
    [RelayCommand] private void DeleteGlossaryTerm() { if (SelectedGlossaryEntry is { } selected) ChangeGlossary(s => s.Glossary.RemoveAll(e => e.Term == selected.Term)); }

    [RelayCommand]
    private async Task ImportGlossary()
    {
        var settings = ProjectSettings;
        if (settings is null || IsAuditMode) return;
        var file = await _dialogService.SelectFileAsync(CurrentPath, "Import glossary CSV", [new FileFilter("CSV", "*.csv")]);
        if (file is null || !ReferenceEquals(settings, ProjectSettings)) return;
        try
        {
            var entries = GlossaryService.ImportCsv(await System.IO.File.ReadAllTextAsync(file));
            if (!ReferenceEquals(settings, ProjectSettings)) return;
            ChangeGlossary(s => { foreach (var entry in entries) { s.Glossary.RemoveAll(e => e.Term.Equals(entry.Term, StringComparison.OrdinalIgnoreCase)); s.Glossary.Add(entry); } });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or FormatException)
        { await _messageService.ShowMessageAsync(ex.Message, "Import glossary"); }
    }

    [RelayCommand]
    private async Task ExportGlossary()
    {
        if (ProjectSettings is not { } settings) return;
        var csv = GlossaryService.ExportCsv(settings.Glossary);
        var file = await _dialogService.SaveFileAsync(CurrentPath, "glossary.csv", "Export glossary CSV", [new FileFilter("CSV", "*.csv")]);
        if (file is null) return;
        try { await System.IO.File.WriteAllTextAsync(file, csv); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { await _messageService.ShowMessageAsync(ex.Message, "Export glossary"); }
    }

    [RelayCommand]
    private async Task InsertGlossaryTranslation(GlossaryEntry? entry)
    {
        var key = SelectedGroup?.Namespace ?? SelectedNode?.Namespace;
        if (entry is null || entry.Forbidden || key is null || IsAuditMode) return;
        var targets = ItemsForEditorKey(key).Where(i => i.Language != PrimaryLanguage && entry.Translations.ContainsKey(i.Language)).ToList();
        var settings = ProjectSettings; var revision = _workspaceRevision;
        var language = await _dialogService.ShowPickAsync("Insert glossary term", "Choose target language", targets.Select(t => t.Language).ToList());
        if (language is null || revision != _workspaceRevision || !ReferenceEquals(settings, ProjectSettings) || IsAuditMode) return;
        FlushPendingEdits();
        var target = targets.First(t => t.Language == language);
        if (!target.Permissions.EditValues) return;
        var before = CaptureEntries([target]);
        target.Value += entry.Translations[language];
        target.IsApproved = false; target.ApprovedAtUtc = null;
        RecordEntryChanges(before); NotifyBulkValueChanges([target]);
    }
}
