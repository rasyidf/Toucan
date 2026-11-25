using System.Text.Json;
using System.Text.Json.Serialization;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>What a reviewer did to a translation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewAction>))]
public enum ReviewAction { Approved, Rejected, Unapproved }

/// <summary>
/// One review action. <see cref="Actor"/> is the local account name of whoever ran Toucan: it identifies a machine
/// user, not an authenticated team member.
/// </summary>
public sealed record ReviewEvent(DateTime Utc, ReviewAction Action, string Key, string Language, string Actor, string? Comment = null);

/// <summary>
/// Review history of a project, kept in <c>.toucan/review-history.jsonl</c> (one event per line, append-only).
/// Actions are held in memory until the project is saved, so unsaved or undone actions never reach the file.
/// </summary>
public sealed class ReviewHistory
{
    public const string RelativePath = ".toucan/review-history.jsonl";
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private readonly List<ReviewEvent> _pending = [];
    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;
    private readonly string _actor;

    public ReviewHistory(Func<DateTime>? clock = null, string? actor = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        _actor = actor ?? Environment.UserName;
    }

    /// <summary>Actions recorded since the last save.</summary>
    public IReadOnlyList<ReviewEvent> Pending { get { lock (_gate) return [.. _pending]; } }

    public void Record(ReviewAction action, TranslationItem item, string? comment = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        lock (_gate) _pending.Add(new(_clock(), action, item.Namespace, item.Language, _actor, text));
    }

    /// <summary>Forgets unsaved actions, for example when the project closes without saving.</summary>
    public void Discard() { lock (_gate) _pending.Clear(); }

    /// <summary>
    /// Appends the actions still reflected by <paramref name="current"/> to the history file in
    /// <paramref name="projectRoot"/>. For each translation, the latest actions whose result the translation no
    /// longer shows (they were undone) are dropped; earlier actions followed by a kept one stay.
    /// Call after the project saved successfully.
    /// </summary>
    /// <returns>Number of events written.</returns>
    public int Flush(string projectRoot, IEnumerable<TranslationItem> current)
    {
        ReviewEvent[] events;
        lock (_gate) { events = [.. _pending]; _pending.Clear(); }
        if (events.Length == 0) return 0;
        var state = current.GroupBy(i => (i.Namespace, i.Language)).ToDictionary(g => g.Key, g => g.First());
        var kept = new List<ReviewEvent>();
        foreach (var group in events.GroupBy(e => (e.Key, e.Language)))
        {
            if (!state.TryGetValue(group.Key, out var item)) continue;
            var list = group.ToList();
            while (list.Count > 0 && !Holds(list[^1].Action, item)) list.RemoveAt(list.Count - 1);
            kept.AddRange(list);
        }
        kept.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        if (kept.Count == 0) return 0;
        var path = Path.Combine(projectRoot, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllLines(path, kept.Select(e => JsonSerializer.Serialize(e, s_json)));
        return kept.Count;
    }

    private static bool Holds(ReviewAction action, TranslationItem item) => action switch
    {
        ReviewAction.Approved => item.IsApproved,
        ReviewAction.Rejected => !item.IsApproved && item.NeedsReview,
        _ => !item.IsApproved,
    };

    /// <summary>
    /// Saved and pending events for one translation, oldest first. Lines that cannot be read are skipped so a
    /// damaged history never blocks the editor.
    /// </summary>
    public IReadOnlyList<ReviewEvent> For(string projectRoot, string key, string language)
    {
        var result = new List<ReviewEvent>();
        var path = Path.Combine(projectRoot, RelativePath);
        if (File.Exists(path))
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ReviewEvent? e;
                try { e = JsonSerializer.Deserialize<ReviewEvent>(line, s_json); }
                catch (JsonException) { continue; }
                if (e is not null && e.Key == key && e.Language == language) result.Add(e);
            }
        lock (_gate) result.AddRange(_pending.Where(e => e.Key == key && e.Language == language));
        return result;
    }
}
