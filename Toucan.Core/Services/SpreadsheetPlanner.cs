using System.Text.Json;
using Toucan.Core.Models;
using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Services;

public sealed record SpreadsheetChange(int Row, string Package, string Key, string Namespace, string Language, string Kind,
    string? Before, string? After, string? BeforeComment, string? AfterComment, bool BeforeApproved, bool AfterApproved);

/// <summary>A detached proposed workspace. A conflict blocks the whole import.</summary>
public sealed record SpreadsheetPreview(IReadOnlyList<TranslationItem> Items, IReadOnlyList<SpreadsheetChange> Changes,
    IReadOnlyList<string> Conflicts)
{
    public bool CanApply => Conflicts.Count == 0 && Changes.Count > 0;
    public int Additions => Changes.Count(c => c.Kind == "Addition");
    public int Edits => Changes.Count(c => c.Kind == "Edit");
    public int Clears => Changes.Count(c => c.Kind == "Clear");
    public int MetadataOnly => Changes.Count(c => c.Kind == "Metadata only");
    public string Summary => $"{Additions} additions, {Edits} value edits, {Clears} clears, {MetadataOnly} metadata-only changes, {Conflicts.Count} conflicts.";
}

public static class SpreadsheetPlanner
{
    public static SpreadsheetPreview Plan(SpreadsheetTable table, IEnumerable<TranslationItem> current, IEnumerable<ResourceDescriptor>? resources = null)
    {
        var items = current.Select(i => ResourceEntryOperations.Copy(i)).ToList();
        var documents = (resources ?? []).ToList();
        var changes = new List<SpreadsheetChange>();
        var conflicts = new List<string>();
        var packages = items.Select(ExcelService.Package).Concat(documents.Select(d => d.Package)).ToHashSet(StringComparer.Ordinal);
        var languages = items.Select(i => i.Language).Concat(documents.Select(d => d.Language)).ToHashSet(StringComparer.Ordinal);
        if (table.Units.Count > ExcelService.MaxUnits) throw new FormatException("Too many spreadsheet units.");
        if (!table.HasPackageColumn && (packages.Count != 1 || !packages.Contains("main")))
            return new(items, [], ["This workbook has no Package column. Add explicit package names and resource keys before importing into a package-aware project."]);
        var index = items.GroupBy(i => (ExcelService.Package(i), ExcelService.Key(i), i.Language)).ToDictionary(g => g.Key, g => g.ToList());
        var destinationsByLanguage = documents.GroupBy(d => (d.Package, d.Language)).ToDictionary(g => g.Key, g => g.ToList());
        var displayKeys = items.Select(i => (i.Namespace, i.Language)).ToHashSet();
        var values = new HashSet<(string Resource, string Path)>();
        var parents = new HashSet<(string Resource, string Path)>();
        foreach (var item in items) IndexPath(item.Identity);
        void IndexPath(ResourceEntryIdentity? identity)
        {
            if (identity == null) return;
            values.Add((identity.ResourceId, identity.KeyToken));
            foreach (var prefix in Prefixes(identity.Path)) parents.Add((identity.ResourceId, prefix));
        }
        var touched = new HashSet<TranslationItem>(ReferenceEqualityComparer.Instance);
        foreach (var unit in table.Units)
        {
            void Conflict(string reason) => conflicts.Add($"Row {unit.Row}: {unit.Package}/{unit.Key} [{unit.Language}]: {reason}");
            if (!packages.Contains(unit.Package)) { Conflict("Unknown package. Configure its resource destination first."); continue; }
            if (!languages.Contains(unit.Language)) { Conflict("Unknown language. Add it to the project first (locale names are exact)."); continue; }
            var token = unit.Path == null ? null : JsonSerializer.Serialize(unit.Path);
            var candidates = index.GetValueOrDefault((unit.Package, unit.Key, unit.Language), [])
                .Where(i => token == null || i.Identity?.KeyToken == token).ToList();
            if (candidates.Count > 1) { Conflict("Ambiguous key; use a Toucan typed-path workbook or separate the resources."); continue; }
            var existing = candidates.SingleOrDefault();
            var added = existing == null;
            if (existing == null)
            {
                if (items.Any(i => !i.Permissions.EditKeys)) { Conflict("Resource capabilities forbid adding keys."); continue; }
                var destinations = destinationsByLanguage.GetValueOrDefault((unit.Package, unit.Language), []);
                if (destinations.Count != 1) { Conflict("No unique resource destination for this addition."); continue; }
                existing = new TranslationItem { Namespace = unit.Key, Language = unit.Language, Value = "" };
                ResourceEntryOperations.Move(existing, destinations[0], unit.Key);
                if (unit.Path != null)
                {
                    if ((destinations[0].Options.Layout is ResourceLayout.Literal or ResourceLayout.Dotted || destinations[0].FormatId is FormatIds.Arb or FormatIds.JavaProperties or FormatIds.Resx) &&
                        unit.Path is not [{ Property: not null }]) { Conflict("The destination requires literal keys."); continue; }
                    ResourceEntryOperations.RenameKey(existing, unit.Path);
                }
                if (displayKeys.Contains((existing.Namespace, existing.Language))) { Conflict("Addition collides with an existing display key."); continue; }
                if (parents.Contains((existing.Identity!.ResourceId, existing.Identity.KeyToken)) ||
                    Prefixes(existing.Identity.Path).Any(p => values.Contains((existing.Identity.ResourceId, p))))
                { Conflict("Addition conflicts with a parent or child resource value."); continue; }
                items.Add(existing);
                IndexPath(existing.Identity);
                displayKeys.Add((existing.Namespace, existing.Language));
                var address = (unit.Package, unit.Key, unit.Language);
                if (!index.TryGetValue(address, out var entries)) index[address] = entries = [];
                entries.Add(existing);
            }
            if (!touched.Add(existing)) { Conflict("Multiple rows resolve to the same entry."); continue; }
            var before = existing.Value;
            var beforeComment = existing.Comment;
            var beforeApproved = existing.IsApproved;
            var valueChanged = unit.Value != null && existing.Value != unit.Value;
            if (valueChanged && !existing.Permissions.EditValues ||
                !existing.Permissions.EditMetadata && (unit.Comment != null && existing.Comment != unit.Comment || unit.Approved != null && existing.IsApproved != unit.Approved))
            { Conflict("Resource capabilities forbid this change."); continue; }
            var metadataChanged = unit.Comment != null && existing.Comment != unit.Comment || unit.Approved != null && existing.IsApproved != unit.Approved;
            if (!added && !valueChanged && !metadataChanged) continue;
            if (unit.Value != null) existing.Value = unit.Value;
            if (unit.Comment != null) existing.Comment = unit.Comment;
            if (unit.Approved != null) existing.IsApproved = unit.Approved.Value;
            existing.ChangeType = ChangeType.External;
            existing.LastModifiedUtc = DateTime.UtcNow;
            if (unit.Approved != null) existing.ApprovedAtUtc = existing.IsApproved ? existing.LastModifiedUtc : null;
            changes.Add(new(unit.Row, unit.Package, unit.Key, existing.Namespace, unit.Language,
                added ? "Addition" : valueChanged ? string.IsNullOrEmpty(unit.Value) ? "Clear" : "Edit" : "Metadata only", before, existing.Value, beforeComment, existing.Comment, beforeApproved, existing.IsApproved));
        }
        return new(items, changes, conflicts);
    }

    private static IEnumerable<string> Prefixes(IReadOnlyList<ResourcePathSegment> path)
    {
        for (var n = 1; n < path.Count; n++) yield return JsonSerializer.Serialize(path.Take(n).ToArray());
    }
}
