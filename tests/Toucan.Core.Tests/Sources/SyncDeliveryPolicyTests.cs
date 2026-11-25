using Toucan.Core.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

public class SyncDeliveryPolicyTests
{
    private static readonly Guid s_bad = Guid.NewGuid();
    private static readonly Guid s_good = Guid.NewGuid();

    private static PushChange Push(Guid entry, string language, PushKind kind = PushKind.Value) =>
        new() { Kind = kind, EntryId = entry, Language = language, Remote = new SyncRemoteKey("default", entry == s_bad ? "bad" : "good"), Value = "x" };

    private static SyncPlan Plan(params PushChange[] push) =>
        new("c1", Guid.NewGuid(), SyncPlanKind.Push, 1, DateTimeOffset.UnixEpoch, true, false, [], push, [], []);

    [Fact]
    public void InvalidValuesAreHeldBackWithANoticeAndTheRestIsSent()
    {
        var plan = SyncDeliveryPolicy.HoldBackInvalid(
            Plan(Push(s_bad, "fr"), Push(s_bad, "fr", PushKind.Status), Push(s_bad, "de"), Push(s_good, "fr")),
            new HashSet<(Guid, string)> { (s_bad, "fr") });

        Assert.Equal([(s_bad, "de"), (s_good, "fr")], plan.Push.Select(c => (c.EntryId, c.Language)));
        var notice = Assert.Single(plan.Notices);
        Assert.Equal(SyncNoticeKind.HeldBackInvalid, notice.Kind);
        Assert.Equal((s_bad, "fr"), (notice.EntryId, notice.Language));
        Assert.Contains("bad [fr]", notice.Message);
    }

    [Fact]
    public void DeletionsStillGoOutAndAnEmptyPolicyChangesNothing()
    {
        var plan = Plan(Push(s_bad, "fr", PushKind.DeleteTranslation));
        Assert.Single(SyncDeliveryPolicy.HoldBackInvalid(plan, new HashSet<(Guid, string)> { (s_bad, "fr") }).Push);
        Assert.Same(plan, SyncDeliveryPolicy.HoldBackInvalid(plan, new HashSet<(Guid, string)>()));
    }
}
