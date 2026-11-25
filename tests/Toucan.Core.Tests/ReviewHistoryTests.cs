using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public sealed class ReviewHistoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-review-history-").FullName;
    private static readonly DateTime s_now = new(2026, 11, 25, 9, 30, 0, DateTimeKind.Utc);
    private readonly ReviewHistory _history = new(() => s_now, "reviewer");

    private static TranslationItem Item(string key, string language, bool approved = false, bool needsReview = false) =>
        new() { Namespace = key, Language = language, Value = "x", IsApproved = approved, NeedsReview = needsReview };

    [Fact]
    public void Nothing_is_written_until_flush_and_flush_appends()
    {
        var fr = Item("title", "fr", approved: true);
        _history.Record(ReviewAction.Approved, fr, "  Looks right  ");
        Assert.False(File.Exists(Path.Combine(_root, ReviewHistory.RelativePath)));

        Assert.Equal(1, _history.Flush(_root, [fr]));
        fr.IsApproved = false; fr.NeedsReview = true;
        _history.Record(ReviewAction.Rejected, fr);
        Assert.Equal(1, _history.Flush(_root, [fr]));

        var events = _history.For(_root, "title", "fr");
        Assert.Equal([ReviewAction.Approved, ReviewAction.Rejected], events.Select(e => e.Action));
        Assert.Equal("Looks right", events[0].Comment);
        Assert.Null(events[1].Comment);
        Assert.All(events, e => Assert.Equal("reviewer", e.Actor));
        Assert.Empty(_history.Pending);
    }

    [Fact]
    public void Actions_that_were_undone_are_not_written()
    {
        var fr = Item("title", "fr");
        _history.Record(ReviewAction.Approved, fr);
        Assert.Equal(0, _history.Flush(_root, [fr]));
        Assert.Empty(_history.For(_root, "title", "fr"));
    }

    [Fact]
    public void An_undone_rejection_is_dropped_but_the_approval_before_it_is_kept()
    {
        var fr = Item("title", "fr", approved: true);
        _history.Record(ReviewAction.Approved, fr);
        _history.Record(ReviewAction.Rejected, fr);
        Assert.Equal(1, _history.Flush(_root, [fr]));
        Assert.Equal([ReviewAction.Approved], _history.For(_root, "title", "fr").Select(e => e.Action));
    }

    [Fact]
    public void Pending_actions_show_before_saving_and_discard_forgets_them()
    {
        var fr = Item("title", "fr", approved: true);
        _history.Record(ReviewAction.Approved, fr);
        Assert.Single(_history.For(_root, "title", "fr"));
        _history.Discard();
        Assert.Empty(_history.For(_root, "title", "fr"));
    }

    [Fact]
    public void Damaged_lines_are_skipped()
    {
        var path = Path.Combine(_root, ReviewHistory.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{not json\n");
        var fr = Item("title", "fr", approved: true);
        _history.Record(ReviewAction.Approved, fr);
        _history.Flush(_root, [fr]);
        Assert.Single(_history.For(_root, "title", "fr"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
