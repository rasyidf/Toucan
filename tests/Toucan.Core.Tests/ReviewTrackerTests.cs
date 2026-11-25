using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Models;
using Toucan.Core.Tests.Formats;
using Toucan.Extensions;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public class ReviewTrackerTests
{
    private static TranslationItem Item(string lang, string key, string value, bool approved = false) =>
        new() { Language = lang, Namespace = key, Value = value, IsApproved = approved };

    [Fact]
    public void Status_is_draft_needs_review_or_approved()
    {
        var item = Item("id", "k", "Halo");
        Assert.Equal(ReviewStatus.Draft, item.Review);
        item.NeedsReview = true;
        Assert.Equal(ReviewStatus.NeedsReview, item.Review);
        item.IsApproved = true;
        Assert.Equal(ReviewStatus.Approved, item.Review);
    }

    [Fact]
    public void Approve_records_source_hash_and_clears_needs_review()
    {
        var target = Item("id", "k", "Halo");
        target.NeedsReview = true;
        ReviewTracker.Approve(target, "Hello");
        Assert.True(target.IsApproved);
        Assert.False(target.NeedsReview);
        Assert.NotNull(target.ApprovedAtUtc);
        Assert.Equal(ReviewTracker.Hash("Hello"), target.ReviewedSourceHash);
    }

    [Fact]
    public void Reconcile_finds_the_source_by_base_language_like_the_desktop()
    {
        var source = Item("en", "k", "Hello");
        var target = Item("de", "k", "Hallo");
        ReviewTracker.Approve(target, "Hello");
        Assert.Equal(0, ReviewTracker.Reconcile([source, target], "en-US"));
        Assert.True(target.IsApproved);
        source.Value = "Hello there";
        Assert.Equal(1, ReviewTracker.Reconcile([source, target], "en-US"));
    }

    [Fact]
    public void Reconcile_without_any_source_text_withdraws_nothing()
    {
        var target = Item("de", "k", "Hallo");
        ReviewTracker.Approve(target, "Hello");
        Assert.Equal(0, ReviewTracker.Reconcile([target], "fr"));
        Assert.True(target.IsApproved);
    }

    [Fact]
    public void Invalidate_moves_an_approved_target_to_needs_review_and_ignores_drafts()
    {
        var approved = Item("id", "k", "Halo");
        ReviewTracker.Approve(approved, "Hello");
        Assert.True(ReviewTracker.Invalidate(approved));
        Assert.Equal(ReviewStatus.NeedsReview, approved.Review);
        Assert.Null(approved.ApprovedAtUtc);

        var draft = Item("id", "k2", "Halo");
        Assert.False(ReviewTracker.Invalidate(draft));
        Assert.Equal(ReviewStatus.Draft, draft.Review);
    }

    [Fact]
    public void Reconcile_withdraws_approvals_whose_source_changed()
    {
        var source = Item("en", "k", "Hello");
        var target = Item("id", "k", "Halo");
        ReviewTracker.Approve(target, "Hello");
        source.Value = "Hello there";
        Assert.Equal(1, ReviewTracker.Reconcile([source, target], "en"));
        Assert.Equal(ReviewStatus.NeedsReview, target.Review);
    }

    [Fact]
    public void Reconcile_keeps_approvals_whose_source_is_unchanged()
    {
        var source = Item("en", "k", "Hello");
        var target = Item("id", "k", "Halo");
        ReviewTracker.Approve(target, "Hello");
        Assert.Equal(0, ReviewTracker.Reconcile([source, target], "en"));
        Assert.Equal(ReviewStatus.Approved, target.Review);
    }

    [Fact]
    public void Reconcile_gives_legacy_approvals_a_baseline_without_withdrawing_them()
    {
        var source = Item("en", "k", "Hello");
        var legacy = Item("id", "k", "Halo", approved: true);
        Assert.Equal(0, ReviewTracker.Reconcile([source, legacy], "en"));
        Assert.Equal(ReviewStatus.Approved, legacy.Review);
        Assert.Equal(ReviewTracker.Hash("Hello"), legacy.ReviewedSourceHash);
        source.Value = "Changed";
        Assert.Equal(1, ReviewTracker.Reconcile([source, legacy], "en"));
    }

    [Fact]
    public void OnSourceChanged_only_affects_targets_of_that_key()
    {
        var a = Item("id", "a", "A", approved: true);
        var b = Item("id", "b", "B", approved: true);
        var sourceItem = Item("en", "a", "A", approved: true);
        Assert.Equal(1, ReviewTracker.OnSourceChanged([a, b, sourceItem], "en", "a"));
        Assert.Equal(ReviewStatus.NeedsReview, a.Review);
        Assert.Equal(ReviewStatus.Approved, b.Review);
        Assert.Equal(ReviewStatus.Approved, sourceItem.Review);
    }

    [Fact]
    public void Review_state_survives_manifest_states()
    {
        var settings = new ProjectSettings();
        settings.TranslationStates.Add(new TranslationState { Language = "id", Key = "k", Approved = false, NeedsReview = true, ReviewedSourceHash = "abc" });
        var item = Item("id", "k", "Halo");
        ProjectService.RestoreStates(settings, [item]);
        Assert.Equal(ReviewStatus.NeedsReview, item.Review);
        Assert.Equal("abc", item.ReviewedSourceHash);
    }

    [Fact]
    public void Approval_survives_save_and_reload_and_is_withdrawn_when_the_source_file_changes()
    {
        var folder = Directory.CreateTempSubdirectory("toucan-review-").FullName;
        try
        {
            var service = new ProjectService(new Toucan.Core.Services.FileService(NullLogger<Toucan.Core.Services.FileService>.Instance), FormatTestHost.SaveStrategies, FormatTestHost.Factory,
                new ProjectModeResolver(), NullLogger<ProjectService>.Instance);
            var project = service.CreateProject(folder, ["en", "id"], "json");
            project.PrimaryLanguage = "en";
            var source = Item("en", "app.title", "Hello");
            var target = Item("id", "app.title", "Halo");
            ReviewTracker.Approve(target, source.Value);
            List<TranslationItem> items = [source, target];
            service.Save(project, items.ToNsTree().ToList(), items);

            var reloaded = service.LoadProject(folder).Translations;
            Assert.Equal(ReviewStatus.Approved, reloaded.Single(i => i.Language == "id").Review);

            var sourceFile = service.GetLanguageFiles(project, "en").Single();
            File.WriteAllText(sourceFile, File.ReadAllText(sourceFile).Replace("Hello", "Hello world"));
            var changed = service.LoadProject(folder).Translations;
            Assert.Equal(ReviewStatus.NeedsReview, changed.Single(i => i.Language == "id").Review);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
