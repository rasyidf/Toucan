using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Sources;
using Toucan.Plugins;

namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// The two status bar indicators: whether the files are saved locally and whether the content sources have the changes.
/// Saving and delivering are separate, so one never stands in for the other.
/// </summary>
public sealed partial class SyncIndicators : ObservableObject
{
    [ObservableProperty] private string savedText = string.Empty;
    [ObservableProperty] private string syncedText = string.Empty;
    [ObservableProperty] private bool hasConnections;
    [ObservableProperty] private bool hasSyncProblem;
    [ObservableProperty] private bool isSyncing;
    [ObservableProperty] private string syncedToolTip = string.Empty;

    public void UpdateSaved(bool isDirty) => SavedText = isDirty ? "Unsaved changes" : "Saved locally";

    public void UpdateSynced(IReadOnlyList<SyncStatus> statuses)
    {
        HasConnections = statuses.Count > 0;
        if (statuses.Count == 0) { SyncedText = string.Empty; SyncedToolTip = string.Empty; HasSyncProblem = false; IsSyncing = false; return; }
        var pending = statuses.Sum(s => s.PendingChanges);
        var conflicts = statuses.Sum(s => s.Conflicts);
        IsSyncing = statuses.Any(s => s.State != SyncRunState.Idle);
        HasSyncProblem = conflicts > 0 || statuses.Any(s => s.NeedsSignIn || s.PartialFailure);
        SyncedText = IsSyncing ? "Syncing…" : conflicts > 0 ? $"{conflicts} conflicts" : statuses.Any(s => s.NeedsSignIn) ? "Sign in needed" : pending > 0 ? $"{pending} not synced" : "Synced";
        SyncedToolTip = string.Join("\n", statuses.Select(s => $"{s.Name}: {s.Message}".TrimEnd(' ', ':')));
    }

    /// <summary>Short words and glyph names for a row badge.</summary>
    public static string BadgeText(SourceDeliveryState? state) => state switch
    {
        SourceDeliveryState.Pending => "Not synced yet",
        SourceDeliveryState.Sending => "Sending to the source",
        SourceDeliveryState.Failed => "Sending failed",
        SourceDeliveryState.Conflict => "Conflict with the source",
        _ => string.Empty,
    };
}
