using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toucan.Core.Sources;

namespace Toucan.Avalonia.ViewModels;

/// <summary>One change in the plan preview, with the checkbox that includes it.</summary>
public sealed partial class PlanRowViewModel(Guid id, string direction, string action, string key, string language, string detail, bool include) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Direction { get; } = direction;
    public string Action { get; } = action;
    public string Key { get; } = key;
    public string Language { get; } = language;
    public string Detail { get; } = detail;
    public string AccessibleName => $"{Direction}: {Action} {Key} ({Language})";

    [ObservableProperty] private bool include = include;
}

/// <summary>One conflict: base, local and remote side by side, and what the user decided.</summary>
public sealed partial class ConflictRowViewModel : ObservableObject
{
    public ConflictRowViewModel(SyncConflict conflict)
    {
        Conflict = conflict;
        mergedText = conflict.Local ?? conflict.RemoteValue ?? string.Empty;
    }

    public SyncConflict Conflict { get; }
    public string Key => Conflict.Remote.Name;
    public string Language => Conflict.Language;
    public string FieldText => Conflict.Field.ToString();
    public string KindText => Conflict.Kind switch
    {
        SyncConflictKind.EditedLocallyDeletedRemotely => "Changed here, deleted there",
        SyncConflictKind.DeletedLocallyEditedRemotely => "Deleted here, changed there",
        SyncConflictKind.FirstSync => "Differs (no common version yet)",
        _ => "Changed on both sides",
    };
    public string BaseText => Conflict.Base ?? "(none)";
    public string LocalText => Conflict.Local ?? "(deleted)";
    public string RemoteText => Conflict.RemoteValue ?? "(deleted)";
    public string AccessibleName => $"Conflict in {Key} ({Language}), {KindText}";

    [ObservableProperty] private SyncChoice choice = SyncChoice.Defer;
    [ObservableProperty] private string mergedText;

    public bool IsMerged => Choice == SyncChoice.Merged;
    public string ChoiceText => Choice switch
    {
        SyncChoice.TakeLocal => "Keeping local",
        SyncChoice.TakeRemote => "Taking remote",
        SyncChoice.Merged => "Using merged text",
        _ => "Deferred",
    };

    partial void OnChoiceChanged(SyncChoice value)
    {
        OnPropertyChanged(nameof(IsMerged));
        OnPropertyChanged(nameof(ChoiceText));
    }

    [RelayCommand] private void TakeLocal() => Choice = SyncChoice.TakeLocal;
    [RelayCommand] private void TakeRemote() => Choice = SyncChoice.TakeRemote;
    [RelayCommand] private void EditMerged() => Choice = SyncChoice.Merged;
    [RelayCommand] private void Defer() => Choice = SyncChoice.Defer;

    public SyncResolution ToResolution() => new(Conflict.Id, Choice, Choice == SyncChoice.Merged ? MergedText : null);
}

/// <summary>The plan preview: pull, push, conflicts and notices, each change with an include toggle. Builds the plan to apply.</summary>
public sealed partial class SyncPlanViewModel : ObservableObject
{
    private readonly SyncPlan _plan;

    public SyncPlanViewModel(SyncPlan plan, string connectionName)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ConnectionName = connectionName;
        foreach (var c in plan.Pull.Where(c => c.Kind != PullKind.Converge))
            PullRows.Add(new(c.Id, "Pull", c.Kind switch { PullKind.Create => "Add", PullKind.Delete => "Delete", PullKind.Rename => "Rename", _ => "Update" },
                c.Remote.Name, c.Language, Describe(c), c.Include));
        foreach (var c in plan.Push)
            PushRows.Add(new(c.Id, "Push", c.Kind switch
            {
                PushKind.Create => "Create", PushKind.DeleteKey or PushKind.DeleteTranslation => "Delete", PushKind.Rename => "Rename", _ => "Send",
            }, c.Remote.Name, c.Language, c.Value ?? c.Comment ?? c.NewName ?? c.Status?.ToString() ?? string.Empty, c.Include));
        foreach (var c in plan.Conflicts) Conflicts.Add(new(c));
        foreach (var n in plan.Notices) Notices.Add(n.Message);
        foreach (var row in PullRows.Concat(PushRows)) row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
        foreach (var row in Conflicts) row.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public string ConnectionName { get; }

    /// <summary>Opens on the conflict list (from a row badge or the Show conflicts command).</summary>
    public bool StartOnConflicts { get; init; }
    public string Title => $"Sync with {ConnectionName}";
    public ObservableCollection<PlanRowViewModel> PullRows { get; } = [];
    public ObservableCollection<PlanRowViewModel> PushRows { get; } = [];
    public ObservableCollection<ConflictRowViewModel> Conflicts { get; } = [];
    public ObservableCollection<string> Notices { get; } = [];

    public bool IsFirstSync => _plan.IsFirstSync;
    public bool HasPull => PullRows.Count > 0;
    public bool HasPush => PushRows.Count > 0;
    public bool HasConflicts => Conflicts.Count > 0;
    public bool HasNotices => Notices.Count > 0;
    public bool IsPartialRead => !_plan.RemoteReadComplete;

    public string Summary
    {
        get
        {
            var pull = PullRows.Count(r => r.Include);
            var push = PushRows.Count(r => r.Include);
            var open = Conflicts.Count(c => c.Choice == SyncChoice.Defer);
            return $"{pull} to pull · {push} to push · {open} conflicts left undecided";
        }
    }

    /// <summary>Set when the dialog closes with Apply.</summary>
    public bool Confirmed { get; private set; }

    public event EventHandler? CloseRequested;

    [RelayCommand] private void UseRemoteForAll() { foreach (var c in Conflicts) c.Choice = SyncChoice.TakeRemote; }
    [RelayCommand] private void UseLocalForAll() { foreach (var c in Conflicts) c.Choice = SyncChoice.TakeLocal; }
    [RelayCommand] private void DeferAll() { foreach (var c in Conflicts) c.Choice = SyncChoice.Defer; }
    [RelayCommand] private void IncludeAll() { foreach (var r in PullRows.Concat(PushRows)) r.Include = true; }
    [RelayCommand] private void ExcludeAll() { foreach (var r in PullRows.Concat(PushRows)) r.Include = false; }

    [RelayCommand]
    private void Apply()
    {
        Confirmed = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The plan with the user's exclusions and conflict decisions applied. Deferred conflicts stay in it.</summary>
    public SyncPlan BuildPlan()
    {
        var excluded = PullRows.Concat(PushRows).Where(r => !r.Include).Select(r => r.Id).ToArray();
        var included = PullRows.Concat(PushRows).Where(r => r.Include).Select(r => r.Id).ToArray();
        return SyncPlanResolver.Resolve(_plan.Include(included).Exclude(excluded), Conflicts.Select(c => c.ToResolution()));
    }

    private static string Describe(PullChange c) => c.Field switch
    {
        SyncField.Status => c.NewStatus?.ToString() ?? string.Empty,
        SyncField.Comment => c.NewComment ?? string.Empty,
        _ => c.NewValue ?? (c.Kind == PullKind.Delete ? "(removed at the service)" : string.Empty),
    };
}
