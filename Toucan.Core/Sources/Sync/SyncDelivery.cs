using Toucan.Plugins;

namespace Toucan.Core.Sources;

/// <summary>Computes where each translation stands with the service, from the journal and the baseline (design 7.6).</summary>
public static class SyncDelivery
{
    public static IReadOnlyDictionary<(Guid EntryId, string Language), SourceDeliveryState> Compute(SyncBaseline? baseline, SyncJournal journal,
        IEnumerable<SyncLocalUnit> local)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(local);
        var byBaseline = (baseline?.Units ?? []).ToDictionary(u => (u.EntryId, u.Language));
        var result = new Dictionary<(Guid, string), SourceDeliveryState>();
        var journaled = new Dictionary<(Guid, string), SourceDeliveryState>();
        foreach (var entry in journal.Entries)
        {
            var state = entry.State switch
            {
                JournalState.Conflict => SourceDeliveryState.Conflict,
                JournalState.Failed => SourceDeliveryState.Failed,
                JournalState.Confirmed => (SourceDeliveryState?)null,
                _ => SourceDeliveryState.Sending,
            };
            if (state is null) continue;
            foreach (var member in entry.Members.Count > 0 ? entry.Members.Select(m => (m.EntryId, m.Language)) : [(entry.EntryId, entry.Language)])
                journaled[member] = Worst(journaled.GetValueOrDefault(member, SourceDeliveryState.Synced), state.Value);
        }
        foreach (var unit in local)
        {
            var key = (unit.EntryId, unit.Language);
            if (journaled.TryGetValue(key, out var inJournal)) { result[key] = inJournal; continue; }
            result[key] = byBaseline.TryGetValue(key, out var b) && b.ValueHash == BaselineUnit.Hash(unit.Value)
                ? SourceDeliveryState.Synced : SourceDeliveryState.Pending;
        }
        return result;
    }

    /// <summary>The state to show when several connections disagree: the one needing the most attention.</summary>
    public static SourceDeliveryState Worst(SourceDeliveryState a, SourceDeliveryState b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(SourceDeliveryState s) => s switch
    {
        SourceDeliveryState.Synced => 0,
        SourceDeliveryState.Pending => 1,
        SourceDeliveryState.Sending => 2,
        SourceDeliveryState.Failed => 3,
        _ => 4,
    };
}
