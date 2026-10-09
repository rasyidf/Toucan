using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>The project as the planner sees it.</summary>
/// <param name="Items">Every translation. The planner works on copies and never changes these.</param>
public sealed record PlanInput(IReadOnlyList<TranslationItem> Items, IReadOnlyList<string> Languages, string PrimaryLanguage, long Revision,
    bool RequireValidForApproval, IValidationPipeline? Validation);

/// <summary>What an edit will do, decided before anything is touched.</summary>
public sealed class EditPlan
{
    /// <summary>Whether the edit may be applied (it may still have nothing to do).</summary>
    public bool CanApply { get; init; }

    /// <summary>The steps to apply, in order. Steps that were no-ops or refused (partial edits) are left out.</summary>
    public IReadOnlyList<(int Index, EditOperation Operation)> Steps { get; init; } = [];

    public IReadOnlyList<EditIssue> Issues { get; init; } = [];

    /// <summary>Translations the steps change, counted the way <see cref="EditResult.ChangedUnits"/> reports them.</summary>
    public int ChangedUnits { get; init; }
}

/// <summary>
/// Dry-runs a <see cref="WorkspaceEdit"/> on a copy of the project: checks every step (keys and languages exist, nothing
/// collides, expected values still hold, the project has not moved on, the strict-approval policy allows an approval) and
/// says exactly what to apply. The host then applies those steps through its own editing paths, so what is applied is what
/// was checked, and a refused edit has touched nothing.
/// </summary>
public static class WorkspaceEditPlanner
{
    public static EditPlan Plan(PlanInput input, WorkspaceEdit edit)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(edit);

        var issues = new List<EditIssue>();
        if (edit.BasedOnRevision is { } based && based != input.Revision)
        {
            issues.Add(new EditIssue(-1, EditIssueKind.Stale, "The project changed since this edit was prepared. Take a new snapshot and prepare it again."));
            return new EditPlan { CanApply = false, Issues = issues };
        }

        var sim = new Simulation(input);
        var steps = new List<(int, EditOperation)>();
        var units = 0;
        var approvals = new List<(int Index, EditOperation Op, string Key, string Language)>();

        for (var i = 0; i < edit.Operations.Count; i++)
        {
            var op = edit.Operations[i];
            var before = issues.Count;
            var changed = Step(sim, i, op, issues);
            if (issues.Count > before) continue; // refused: not part of the plan
            if (changed == 0) continue;           // nothing to do
            steps.Add((i, op));
            units += changed;
            if (op is SetReview { State: ReviewState.Approved } review) approvals.Add((i, op, review.Key, review.Language));
        }

        // The strict-approval policy judges the project as the edit leaves it, so an edit that fixes a value and approves it works.
        if (input.RequireValidForApproval && input.Validation is not null && approvals.Count > 0)
        {
            var errors = input.Validation.RunAll(new ValidationContext { Items = sim.Items, PrimaryLanguage = input.PrimaryLanguage })
                .Where(r => r.Severity == ValidationSeverity.Error)
                .Select(r => (r.Namespace, r.Language)).ToHashSet();
            foreach (var (index, op, key, language) in approvals)
            {
                if (!errors.Contains((key, language)) && !errors.Contains((key, null))) continue;
                issues.Add(new EditIssue(index, EditIssueKind.ApprovalRefused, $"'{key}' [{language}] has validation errors, and this project does not allow approving those."));
                steps.RemoveAll(s => s.Item1 == index);
                units--;
            }
        }

        var canApply = issues.Count == 0 || edit.AllowPartial;
        return new EditPlan { CanApply = canApply, Steps = canApply ? steps : [], Issues = issues, ChangedUnits = canApply ? units : 0 };
    }

    /// <summary>Whether <paramref name="key"/> is usable: dotted segments, none empty or padded with spaces.</summary>
    internal static bool IsValidKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Split('.').All(segment => segment.Length > 0 && segment == segment.Trim());

    /// <summary>Checks and applies one step to the copy. Returns how many translations it changes, 0 for a no-op; adds to <paramref name="issues"/> when refused.</summary>
    private static int Step(Simulation sim, int index, EditOperation op, List<EditIssue> issues)
    {
        switch (op)
        {
            case SetValue set:
            {
                if (!sim.TryUnit(index, set.Key, set.Language, issues, out var unit)) return 0;
                if (set.ExpectedValue is not null && unit.Value != set.ExpectedValue)
                {
                    issues.Add(new EditIssue(index, EditIssueKind.Conflict, $"'{set.Key}' [{set.Language}] changed since it was read."));
                    return 0;
                }
                var value = set.Value ?? string.Empty;
                if (unit.Value == value) return 0;
                unit.Value = value;
                return 1;
            }

            case SetComment comment:
            {
                if (!sim.TryUnit(index, comment.Key, comment.Language, issues, out var unit)) return 0;
                var text = comment.Comment ?? string.Empty;
                if ((unit.Comment ?? string.Empty) == text) return 0;
                unit.Comment = text;
                return 1;
            }

            case SetReview review:
            {
                if (!sim.TryUnit(index, review.Key, review.Language, issues, out var unit)) return 0;
                var approve = review.State == ReviewState.Approved;
                if (unit.IsApproved == approve) return 0;
                if (approve && string.IsNullOrEmpty(unit.Value))
                {
                    issues.Add(new EditIssue(index, EditIssueKind.Empty, $"'{review.Key}' [{review.Language}] is empty and cannot be approved."));
                    return 0;
                }
                unit.IsApproved = approve;
                return 1;
            }

            case AddKey add:
                return StepAdd(sim, index, add, issues);

            case RenameKey rename:
                return StepRename(sim, index, rename, issues);

            case DeleteKey delete:
            {
                var matching = sim.Matching(delete.Key);
                if (matching.Count == 0)
                {
                    issues.Add(new EditIssue(index, EditIssueKind.UnknownKey, $"There is no key '{delete.Key}'."));
                    return 0;
                }
                foreach (var item in matching) sim.Remove(item);
                return matching.Count;
            }

            default:
                issues.Add(new EditIssue(index, EditIssueKind.InvalidKey, $"Unknown kind of step: {op.GetType().Name}."));
                return 0;
        }
    }

    private static int StepAdd(Simulation sim, int index, AddKey add, List<EditIssue> issues)
    {
        if (!IsValidKey(add.Key))
        {
            issues.Add(new EditIssue(index, EditIssueKind.InvalidKey, $"'{add.Key}' is not a valid key: use dotted names with no empty parts."));
            return 0;
        }
        if (sim.Collides(add.Key, except: null))
        {
            issues.Add(new EditIssue(index, EditIssueKind.KeyExists, $"The key '{add.Key}' already exists or conflicts with a group of keys."));
            return 0;
        }
        foreach (var language in add.Values?.Keys ?? [])
        {
            if (sim.Languages.Contains(language, StringComparer.OrdinalIgnoreCase)) continue;
            issues.Add(new EditIssue(index, EditIssueKind.UnknownLanguage, $"The project has no language '{language}'."));
            return 0;
        }

        foreach (var language in sim.Languages)
        {
            var value = add.Values?.FirstOrDefault(v => string.Equals(v.Key, language, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
            sim.Add(new TranslationItem { Namespace = add.Key, Language = language, Value = value });
        }
        return sim.Languages.Count;
    }

    private static int StepRename(Simulation sim, int index, RenameKey rename, List<EditIssue> issues)
    {
        var matching = sim.Matching(rename.Key);
        if (matching.Count == 0)
        {
            issues.Add(new EditIssue(index, EditIssueKind.UnknownKey, $"There is no key '{rename.Key}'."));
            return 0;
        }
        if (!IsValidKey(rename.NewKey) || rename.NewKey == rename.Key || rename.NewKey.StartsWith(rename.Key + ".", StringComparison.Ordinal))
        {
            issues.Add(new EditIssue(index, EditIssueKind.InvalidKey, $"'{rename.NewKey}' is not a valid new name for '{rename.Key}'."));
            return 0;
        }
        if (sim.Collides(rename.NewKey, except: matching))
        {
            issues.Add(new EditIssue(index, EditIssueKind.KeyExists, $"The key '{rename.NewKey}' already exists or conflicts with a group of keys."));
            return 0;
        }

        foreach (var item in matching)
            item.Namespace = item.Namespace == rename.Key ? rename.NewKey : rename.NewKey + item.Namespace[rename.Key.Length..];
        sim.Reindex();
        return matching.Count;
    }

    /// <summary>What the edit left in the project, as findings for the translations it touched.</summary>
    public static IReadOnlyList<EditFinding> Findings(IEnumerable<ValidationResult> results, IReadOnlySet<(string Key, string Language)> touched, IReadOnlySet<string> touchedKeys)
    {
        const int Limit = 50;
        return [.. results
            .Where(r => r.Namespace is not null && (r.Language is null ? touchedKeys.Contains(r.Namespace) : touched.Contains((r.Namespace, r.Language))))
            .Take(Limit)
            .Select(r => new EditFinding(r.Namespace!, r.Language, r.Severity.ToString(), r.Message))];
    }

    /// <summary>A scratch copy of the project that steps are applied to.</summary>
    private sealed class Simulation
    {
        private readonly List<TranslationItem> _items;
        private Dictionary<(string Key, string Language), TranslationItem> _index;

        public Simulation(PlanInput input)
        {
            _items = [.. input.Items.Select(i => new TranslationItem
            {
                Namespace = i.Namespace, Language = i.Language, Value = i.Value ?? string.Empty, Comment = i.Comment ?? string.Empty, IsApproved = i.IsApproved,
            })];
            Languages = input.Languages;
            _index = BuildIndex();
        }

        public IReadOnlyList<string> Languages { get; }

        public IReadOnlyList<TranslationItem> Items => _items;

        private Dictionary<(string, string), TranslationItem> BuildIndex() =>
            _items.GroupBy(i => (i.Namespace, i.Language)).ToDictionary(g => g.Key, g => g.First());

        public void Reindex() => _index = BuildIndex();

        public void Add(TranslationItem item)
        {
            _items.Add(item);
            _index[(item.Namespace, item.Language)] = item;
        }

        public void Remove(TranslationItem item)
        {
            _items.Remove(item);
            _index.Remove((item.Namespace, item.Language));
        }

        /// <summary>The key and every key below it.</summary>
        public List<TranslationItem> Matching(string key) =>
            [.. _items.Where(i => i.Namespace == key || i.Namespace.StartsWith(key + ".", StringComparison.Ordinal))];

        /// <summary>Whether <paramref name="key"/> would clash: it is a key already, is used as a group, or sits below a key (a value cannot also be a group).</summary>
        public bool Collides(string key, IReadOnlyCollection<TranslationItem>? except) =>
            _items.Any(i => (except is null || !except.Contains(i))
                            && (i.Namespace == key || i.Namespace.StartsWith(key + ".", StringComparison.Ordinal) || key.StartsWith(i.Namespace + ".", StringComparison.Ordinal)));

        public bool TryUnit(int index, string key, string language, List<EditIssue> issues, out TranslationItem unit)
        {
            if (_index.TryGetValue((key, language), out unit!)) return true;
            var keyExists = _items.Any(i => i.Namespace == key);
            issues.Add(keyExists
                ? new EditIssue(index, EditIssueKind.UnknownLanguage, $"'{key}' has no translation in '{language}'.")
                : new EditIssue(index, EditIssueKind.UnknownKey, $"There is no key '{key}'."));
            return false;
        }
    }
}
