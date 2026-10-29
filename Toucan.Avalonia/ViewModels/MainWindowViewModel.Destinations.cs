using Toucan.Avalonia.Services;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// Document projects: structural edits choose a destination family (the same document across languages) before they enter the
/// workspace. The user is asked only when more than one family fits; the framework describes every new document path.
/// </summary>
public partial class MainWindowViewModel
{
    private IReadOnlyList<WorkspaceDestinationFamily> DestinationFamilies(params string[] languages) =>
        Workspace.Source is { } source && _documentRouter is not null
            ? _documentRouter.DestinationFamilies(source, ProjectLanguages().Concat(languages).Distinct(StringComparer.Ordinal))
            : [];

    private static string FamilyLabel(WorkspaceDestinationFamily family) =>
        $"{family.Label} ({string.Join(", ", family.ExistingDocuments.Order(StringComparer.Ordinal))})";

    /// <summary>Families whose namespace prefixes <paramref name="segments"/>, longest first; root families when none do.</summary>
    private async Task<(WorkspaceDestinationFamily Family, TranslationPath KeyPath)?> ChooseDestinationAsync(IReadOnlyList<string> segments, string title)
    {
        var families = DestinationFamilies();
        var matching = families.Where(f => f.Namespace.Segments.Count > 0 && f.Namespace.Segments.Count < segments.Count
            && f.Namespace.Segments.SequenceEqual(segments.Take(f.Namespace.Segments.Count), StringComparer.Ordinal)).ToList();
        if (matching.Count > 0)
        {
            var longest = matching.Max(f => f.Namespace.Segments.Count);
            matching = matching.Where(f => f.Namespace.Segments.Count == longest).ToList();
        }
        else matching = families.Where(f => f.Namespace.Segments.Count == 0).ToList();
        if (matching.Count == 0)
        {
            await _messageService.ShowMessageAsync("No source document can hold this key. Start the key with an existing namespace.", title);
            return null;
        }
        var family = matching[0];
        if (matching.Count > 1)
        {
            var labels = matching.Select(FamilyLabel).ToList();
            var picked = await _dialogService.ShowPickAsync(title, "More than one source document can hold this key. Choose where to save it.", labels, labels[0]);
            if (picked is null) return null;
            family = matching[labels.IndexOf(picked)];
        }
        return (family, new TranslationPath(segments.Skip(family.Namespace.Segments.Count).ToArray()));
    }

    private async Task CreateDocumentEntryAsync(string key)
    {
        FlushPendingEdits();
        if (await ChooseDestinationAsync(key.Split('.'), "New Key") is not { } choice) return;
        var result = Workspace.AddDocumentEntry(choice.Family, choice.KeyPath);
        if (!result.Applied) { await _messageService.ShowMessageAsync(result.Issues[0].Message, "New Key"); return; }
        RefreshWorkspaceProjection(true, $"Added key {key}", result.ChangedUnits);
        RevealKey(key);
    }

    private void DuplicateDocumentEntry(Guid id)
    {
        var snapshot = Workspace.SnapshotEntriesAsync().GetAwaiter().GetResult()!;
        var units = snapshot.Units.Where(u => u.EntryId == id).ToList();
        if (units.Count == 0 || Workspace.FamilyOf(id, DestinationFamilies()) is not { } family)
        {
            StatusText = "The entry's source document cannot be determined.";
            return;
        }
        var path = units[0].KeyPath.Segments;
        var taken = snapshot.Units.Where(u => u.Namespace.Equals(family.Namespace)).Select(u => u.KeyPath).ToHashSet();
        TranslationPath copy;
        var n = 1;
        do copy = new(path.SkipLast(1).Append(path[^1] + (n++ == 1 ? "_copy" : $"_copy{n - 1}")).ToArray());
        while (taken.Contains(copy));
        var result = Workspace.AddDocumentEntry(family, copy, units.ToDictionary(u => u.Language, u => u.Value, StringComparer.Ordinal));
        if (!result.Applied) { StatusText = result.Issues[0].Message; return; }
        var key = string.Join('.', family.Namespace.Segments.Concat(copy.Segments));
        RefreshWorkspaceProjection(true, $"Duplicated key {key}", result.ChangedUnits);
        RevealKey(key);
    }

    /// <returns>An error message, or null when the language was added.</returns>
    private string? AddDocumentLanguage(string code)
    {
        FlushPendingEdits();
        var result = Workspace.AddDocumentLanguage(code, DestinationFamilies(code));
        if (!result.Applied) return result.Issues[0].Message;
        if (ProjectSettings is { } settings && !settings.Languages.Contains(code, StringComparer.Ordinal)) settings.Languages.Add(code);
        return result.Issues.Count > 0 ? result.Issues[0].Message : null;
    }

    private string? RemoveDocumentLanguage(string code)
    {
        FlushPendingEdits();
        var result = Workspace.RemoveDocumentLanguage(code);
        if (!result.Applied) return result.Issues[0].Message;
        ProjectSettings?.Languages.RemoveAll(l => string.Equals(l, code, StringComparison.Ordinal));
        return null;
    }

    /// <summary>The workspace already changed; refresh the projection and language-dependent presentation.</summary>
    private void AfterDocumentLanguagesChanged(string status)
    {
        RefreshWorkspaceProjection(true, status);
        if (StatusBarService.Instance.ViewModel is { } sb)
        {
            sb.AvailableLanguages.Clear();
            foreach (var lang in OrderedLanguages()) sb.AvailableLanguages.Add(lang);
        }
        InitLanguageVisibilityFilter(OrderedLanguages());
        StatusText = status;
    }
}
