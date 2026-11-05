using Toucan.Core.Models;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

/// <summary>The three-way comparison of design 7.2, one row at a time.</summary>
public sealed class SyncPlannerTests
{
    private static readonly Guid Entry = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const SourceCapabilities All = SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.RenameKeys
        | SourceCapabilities.DeleteKeys | SourceCapabilities.DeleteTranslations | SourceCapabilities.ConditionalWrites | SourceCapabilities.ReviewStatus | SourceCapabilities.Comments;

    private static SourceConnectionSettings Connection(string deleteRemote = "ask") => new()
    {
        Id = "c1", Adapter = "x.y", Languages = { ["en"] = "en", ["fr"] = "fr" },
        Policy = { DeleteRemote = deleteRemote, PushStatuses = true, PushComments = true },
    };

    private static SyncLocalUnit Local(string key, string value, string language = "fr", Guid? id = null, SourceReviewMeaning status = SourceReviewMeaning.Draft, string comment = "") =>
        new(id ?? Entry, language, TranslationPath.Empty, new TranslationPath(key.Split('.')), value, comment, status);

    private static RemoteUnit Remote(string key, string value, string revision = "1", string language = "fr", SourceReviewMeaning? status = null, string? remoteId = null) =>
        new(new RemoteKey("default", key, remoteId), language, value) { Revision = revision, Status = status is { } s ? new RemoteReviewStatus(s.ToString(), s) : null };

    private static BaselineUnit Base(string key, string value, string language = "fr", Guid? id = null, SourceReviewMeaning? status = null, string revision = "1", string? remoteId = null) => new()
    {
        EntryId = id ?? Entry, Language = language, Remote = new SyncRemoteKey("default", key, remoteId), Value = value, ValueHash = BaselineUnit.Hash(value),
        Status = status, RemoteRevision = revision, CommentHash = BaselineUnit.Hash(string.Empty),
    };

    private static SyncPlan Plan(IEnumerable<SyncLocalUnit> local, IEnumerable<RemoteUnit> remote, IEnumerable<BaselineUnit>? baseline, SyncPlanKind kind = SyncPlanKind.Sync,
        bool complete = true, SourceCapabilities caps = All, string deleteRemote = "ask") =>
        SyncPlanner.Plan(new("c1", Connection(deleteRemote), kind, baseline is null ? null : new SyncBaseline { Units = [.. baseline] }, [.. local], [.. remote], complete, caps, 7, DateTimeOffset.UnixEpoch));

    // ───────────── value rows ─────────────

    [Fact]
    public void NothingChangedPlansNothing()
    {
        var plan = Plan([Local("a", "x")], [Remote("a", "x")], [Base("a", "x")]);
        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Notices);
    }

    [Fact]
    public void LocalEditIsPushedWithTheRevisionItWasBasedOn()
    {
        var plan = Plan([Local("a", "new")], [Remote("a", "x", "5")], [Base("a", "x", revision: "5")]);
        var push = Assert.Single(plan.Push);
        Assert.Equal((PushKind.Value, "new", "5", "x"), (push.Kind, push.Value, push.ExpectedRevision, push.BaseValue));
        Assert.Empty(plan.Pull);
    }

    [Fact]
    public void RemoteEditIsPulledWithTheValueTheProjectHad()
    {
        var plan = Plan([Local("a", "x")], [Remote("a", "new", "6")], [Base("a", "x")]);
        var pull = Assert.Single(plan.Pull);
        Assert.Equal((PullKind.Update, "new", "x", "6"), (pull.Kind, pull.NewValue, pull.LocalValue, pull.RemoteRevision));
        Assert.Empty(plan.Push);
    }

    [Fact]
    public void SameEditOnBothSidesOnlyMovesTheBaseline()
    {
        var plan = Plan([Local("a", "same")], [Remote("a", "same", "9")], [Base("a", "x")]);
        Assert.Equal(PullKind.Converge, Assert.Single(plan.Pull).Kind);
        Assert.False(plan.HasChanges);
        Assert.Empty(plan.Push);
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void DifferentEditsOnBothSidesConflict()
    {
        var plan = Plan([Local("a", "mine")], [Remote("a", "theirs", "9")], [Base("a", "x")]);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal((SyncConflictKind.BothChanged, SyncField.Value, "x", "mine", "theirs"), (conflict.Kind, conflict.Field, conflict.Base, conflict.Local, conflict.RemoteValue));
        Assert.Empty(plan.Push);
        Assert.Empty(plan.Pull);
    }

    // ───────────── existence rows ─────────────

    [Theory]
    [InlineData("always", true, 0)]
    [InlineData("ask", false, 1)]
    [InlineData("never", false, 1)]
    public void LocalDeletionFollowsThePolicy(string policy, bool included, int notices)
    {
        var plan = Plan([], [Remote("a", "x")], [Base("a", "x")], deleteRemote: policy);
        if (policy == "never") { Assert.Empty(plan.Push); Assert.Single(plan.Notices); return; }
        var push = Assert.Single(plan.Push);
        Assert.Equal(PushKind.DeleteKey, push.Kind);
        Assert.Equal(included, push.Include);
        Assert.Equal(notices, plan.Notices.Count);
    }

    [Fact]
    public void DeletedHereButChangedThereIsAConflict()
    {
        var plan = Plan([], [Remote("a", "edited")], [Base("a", "x")]);
        Assert.Equal(SyncConflictKind.DeletedLocallyEditedRemotely, Assert.Single(plan.Conflicts).Kind);
        Assert.Empty(plan.Push);
    }

    [Fact]
    public void DeletedThereAndUnchangedHereIsPulledOnlyWhenIncluded()
    {
        var plan = Plan([Local("a", "x")], [], [Base("a", "x")]);
        var pull = Assert.Single(plan.Pull);
        Assert.Equal(PullKind.Delete, pull.Kind);
        Assert.False(pull.Include);
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void DeletedThereButChangedHereIsAConflict()
    {
        var plan = Plan([Local("a", "mine")], [], [Base("a", "x")]);
        Assert.Equal(SyncConflictKind.EditedLocallyDeletedRemotely, Assert.Single(plan.Conflicts).Kind);
    }

    [Fact]
    public void APartialReadNeverInfersADeletion()
    {
        var plan = Plan([Local("a", "x")], [], [Base("a", "x")], complete: false);
        Assert.Empty(plan.Pull);
        Assert.Empty(plan.Conflicts);
        Assert.Contains(plan.Notices, n => n.Kind == SyncNoticeKind.PartialRead);
        Assert.False(plan.RemoteReadComplete);
    }

    [Fact]
    public void GoneFromBothSidesForgetsTheBaseline()
    {
        var plan = Plan([], [], [Base("a", "x")]);
        Assert.Equal([(Entry, "fr")], plan.Forget);
    }

    [Fact]
    public void NewKeysMoveToTheSideThatLacksThem()
    {
        var other = Guid.NewGuid();
        var plan = Plan([Local("mine", "m", id: other)], [Remote("theirs", "t")], baseline: []);
        var create = Assert.Single(plan.Pull);
        Assert.Equal((PullKind.Create, "theirs", "t"), (create.Kind, create.Remote.Name, create.NewValue));
        var push = Assert.Single(plan.Push);
        Assert.Equal((PushKind.Create, "mine"), (push.Kind, push.Remote.Name));
        Assert.Equal("m", push.Values!["fr"]);
    }

    [Fact]
    public void ANewLocalKeyIsHeldWhenTheServiceCannotCreateKeys()
    {
        var plan = Plan([Local("mine", "m")], [], baseline: [], caps: All & ~SourceCapabilities.CreateKeys);
        Assert.Empty(plan.Push);
        Assert.Contains(plan.Notices, n => n.Kind == SyncNoticeKind.Unsupported);
    }

    // ───────────── first sync ─────────────

    [Fact]
    public void FirstSyncConvergesEqualValuesAndConflictsOnDifferences()
    {
        var other = Guid.NewGuid();
        var plan = Plan([Local("a", "same"), Local("b", "mine", id: other)], [Remote("a", "same"), Remote("b", "theirs")], baseline: null);
        Assert.True(plan.IsFirstSync);
        Assert.Equal(PullKind.Converge, Assert.Single(plan.Pull).Kind);
        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal((SyncConflictKind.FirstSync, null), (conflict.Kind, conflict.Base));
        Assert.Contains(plan.Notices, n => n.Kind == SyncNoticeKind.Info);
    }

    [Theory]
    [InlineData(SyncChoice.TakeRemote, 1, 0)]
    [InlineData(SyncChoice.TakeLocal, 0, 1)]
    public void FirstSyncBulkChoiceSettlesEveryDifference(SyncChoice choice, int pulls, int pushes)
    {
        var plan = Plan([Local("b", "mine")], [Remote("b", "theirs")], baseline: null);
        var settled = SyncPlanner.ResolveAll(plan, choice);
        Assert.Empty(settled.Conflicts);
        Assert.Equal(pulls, settled.Pull.Count(c => c.Kind != PullKind.Converge));
        Assert.Equal(pushes, settled.Push.Count);
    }

    // ───────────── other fields, renames, capabilities ─────────────

    [Fact]
    public void FieldsCompareIndependently()
    {
        var plan = Plan([Local("a", "mine", status: SourceReviewMeaning.Draft)], [Remote("a", "x", "2", status: SourceReviewMeaning.Approved)],
            [Base("a", "x", status: SourceReviewMeaning.Draft)]);
        Assert.Empty(plan.Conflicts);
        Assert.Equal(PushKind.Value, Assert.Single(plan.Push).Kind);
        var pull = Assert.Single(plan.Pull);
        Assert.Equal((SyncField.Status, SourceReviewMeaning.Approved), (pull.Field, pull.NewStatus));
    }

    [Fact]
    public void LocalStatusChangeIsPushedWhenStatusesAreSynced()
    {
        var plan = Plan([Local("a", "x", status: SourceReviewMeaning.Approved)], [Remote("a", "x", status: SourceReviewMeaning.Draft)], [Base("a", "x", status: SourceReviewMeaning.Draft)]);
        Assert.Equal((PushKind.Status, SourceReviewMeaning.Approved), (Assert.Single(plan.Push).Kind, plan.Push[0].Status));
    }

    [Fact]
    public void LocalRenameIsPushedOrHeld()
    {
        var supported = Plan([Local("b", "x")], [Remote("a", "x", remoteId: "k1")], [Base("a", "x", remoteId: "k1")]);
        var rename = Assert.Single(supported.Push);
        Assert.Equal((PushKind.Rename, "b"), (rename.Kind, rename.NewName));

        var held = Plan([Local("b", "x")], [Remote("a", "x", remoteId: "k1")], [Base("a", "x", remoteId: "k1")], caps: All & ~SourceCapabilities.RenameKeys);
        Assert.Empty(held.Push);
        Assert.Contains(held.Notices, n => n.Kind == SyncNoticeKind.Unsupported);
    }

    [Fact]
    public void RemoteRenameIsMatchedByServiceIdAndPulled()
    {
        var plan = Plan([Local("a", "x")], [Remote("renamed", "x", remoteId: "k1")], [Base("a", "x", remoteId: "k1")]);
        var pull = Assert.Single(plan.Pull);
        Assert.Equal(PullKind.Rename, pull.Kind);
        Assert.Equal(["renamed"], pull.KeyPath.Segments);
        Assert.Empty(plan.Push);
    }

    [Fact]
    public void LanguagesWithoutAMappingAreNotSynced()
    {
        var plan = Plan([Local("a", "x", language: "de")], [Remote("a", "y", language: "de")], []);
        Assert.Empty(plan.Push);
        Assert.Empty(plan.Pull);
        Assert.Contains(plan.Notices, n => n.Kind == SyncNoticeKind.UnmappedLanguage);
    }

    [Fact]
    public void PushPlanWithoutConditionalWritesHoldsUnitsTheServiceChanged()
    {
        var other = Guid.NewGuid();
        var plan = Plan([Local("a", "mine"), Local("b", "mine", id: other)], [Remote("a", "x", status: SourceReviewMeaning.Approved), Remote("b", "x")],
            [Base("a", "x", status: SourceReviewMeaning.Draft), Base("b", "x", id: other)], SyncPlanKind.Push, caps: All & ~SourceCapabilities.ConditionalWrites);
        Assert.Equal(other, Assert.Single(plan.Push).EntryId);
        Assert.Contains(plan.Notices, n => n.Kind == SyncNoticeKind.PullFirst);
    }

    [Fact]
    public void PullPlanDropsPushesAndPushPlanDropsPulls()
    {
        var other = Guid.NewGuid();
        Func<SyncPlanKind, SyncPlan> make = kind => Plan([Local("a", "mine"), Local("b", "x", id: other)], [Remote("a", "x"), Remote("b", "theirs")],
            [Base("a", "x"), Base("b", "x", id: other)], kind);
        Assert.Empty(make(SyncPlanKind.Pull).Push);
        Assert.Single(make(SyncPlanKind.Pull).Pull);
        Assert.Empty(make(SyncPlanKind.Push).Pull);
        Assert.Single(make(SyncPlanKind.Push).Push);
    }

    // ───────────── resolving ─────────────

    private static SyncPlan OneConflict() => Plan([Local("a", "mine")], [Remote("a", "theirs", "9")], [Base("a", "x")]);

    [Fact]
    public void TakeRemoteBecomesAPullAndTakeLocalAPushAgainstTheRemoteRevision()
    {
        var plan = OneConflict();
        var remote = SyncPlanResolver.Resolve(plan, [new(plan.Conflicts[0].Id, SyncChoice.TakeRemote)]);
        Assert.Equal("theirs", Assert.Single(remote.Pull).NewValue);
        Assert.Empty(remote.Conflicts);

        var local = SyncPlanResolver.Resolve(plan, [new(plan.Conflicts[0].Id, SyncChoice.TakeLocal)]);
        var push = Assert.Single(local.Push);
        Assert.Equal(("mine", "9"), (push.Value, push.ExpectedRevision));
    }

    [Fact]
    public void MergedTextGoesToBothSides()
    {
        var plan = OneConflict();
        var merged = SyncPlanResolver.Resolve(plan, [new(plan.Conflicts[0].Id, SyncChoice.Merged, "both")]);
        Assert.Equal("both", Assert.Single(merged.Pull).NewValue);
        Assert.Equal("both", Assert.Single(merged.Push).Value);
        Assert.Throws<ArgumentException>(() => SyncPlanResolver.Resolve(plan, [new(plan.Conflicts[0].Id, SyncChoice.Merged)]));
    }

    [Fact]
    public void DeferredConflictsStayAndBlockNothingElse()
    {
        var other = Guid.NewGuid();
        var plan = Plan([Local("a", "mine"), Local("b", "new", id: other)], [Remote("a", "theirs"), Remote("b", "x")], [Base("a", "x"), Base("b", "x", id: other)]);
        var settled = SyncPlanResolver.Resolve(plan, [new(plan.Conflicts[0].Id, SyncChoice.Defer)]);
        Assert.Single(settled.Conflicts);
        Assert.Single(settled.Push);
    }

    [Fact]
    public void ExcludeAndIncludeToggleChangesWithoutMutatingThePlan()
    {
        var plan = Plan([Local("a", "new")], [Remote("a", "x")], [Base("a", "x")]);
        var id = plan.Push[0].Id;
        Assert.Equal(0, plan.Exclude(id).PushCount);
        Assert.Equal(1, plan.PushCount);
        Assert.Equal(1, plan.Exclude(id).Include(id).PushCount);
    }
}
