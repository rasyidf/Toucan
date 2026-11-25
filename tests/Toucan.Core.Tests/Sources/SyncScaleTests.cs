using System.Diagnostics;
using Toucan.Core.Models;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;
using Xunit;
using Xunit.Sdk;

namespace Toucan.Core.Tests.Sources;

/// <summary>
/// 10,000 keys in 10 languages (100,000 translations): planning, state files and a push must stay fast enough that the editor
/// stays responsive. Ceilings are generous so a slow CI machine does not fail them; the timings are written to the test output.
/// </summary>
public sealed class SyncScaleTests : IDisposable
{
    private const int Keys = 10_000;
    private static readonly string[] s_languages = ["en", "fr", "de", "es", "it", "pt", "nl", "pl", "ja", "ko"];
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-scale-").FullName;

    public void Dispose() => TempFolder.TryDelete(_root);

    private static (List<SyncLocalUnit> Local, List<RemoteUnit> Remote, SyncBaseline Baseline) Build(int changedLocally, int changedRemotely)
    {
        var local = new List<SyncLocalUnit>(Keys * s_languages.Length);
        var remote = new List<RemoteUnit>(Keys * s_languages.Length);
        var baseline = new SyncBaseline();
        for (var k = 0; k < Keys; k++)
        {
            var parts = new[] { "area" + k % 50, "screen" + k % 400, "key" + k };
            var name = string.Join('.', parts);
            for (var l = 0; l < s_languages.Length; l++)
            {
                var id = new Guid(k, (short)l, 0, [0, 0, 0, 0, 0, 0, 0, 1]);
                var value = $"Text {k} in {s_languages[l]}";
                var index = k * s_languages.Length + l;
                local.Add(new(id, s_languages[l], TranslationPath.Empty, new TranslationPath(parts), index < changedLocally ? value + " edited" : value, "", SourceReviewMeaning.Draft));
                remote.Add(new(new RemoteKey("default", name, "r" + k), s_languages[l], index >= Keys * s_languages.Length - changedRemotely ? value + " remote" : value) { Revision = "1" });
                baseline.Units.Add(new BaselineUnit { EntryId = id, Language = s_languages[l], Remote = new SyncRemoteKey("default", name, "r" + k), Value = value, ValueHash = BaselineUnit.Hash(value), RemoteRevision = "1" });
            }
        }
        return (local, remote, baseline);
    }

    private static SourceConnectionSettings Connection()
    {
        var connection = new SourceConnectionSettings { Id = "c1", Adapter = "x.y" };
        foreach (var l in s_languages) connection.Languages[l] = l;
        return connection;
    }

    private static T Timed<T>(string what, TimeSpan ceiling, Func<T> work)
    {
        var watch = Stopwatch.StartNew();
        var result = work();
        watch.Stop();
        TestContext.Current.TestOutputHelper!.WriteLine($"{what}: {watch.Elapsed.TotalMilliseconds:F0} ms");
        Assert.True(watch.Elapsed < ceiling, $"{what} took {watch.Elapsed.TotalSeconds:F1}s, ceiling {ceiling.TotalSeconds}s.");
        return result;
    }

    [Fact]
    public void PlanningHundredThousandTranslationsTakesSeconds()
    {
        var (local, remote, baseline) = Build(changedLocally: 1_000, changedRemotely: 1_000);

        var plan = Timed("plan (100k units, 1k local + 1k remote changes)", TimeSpan.FromSeconds(20), () =>
            SyncPlanner.Plan(new("c1", Connection(), SyncPlanKind.Sync, baseline, local, remote, true, SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.ConditionalWrites, 1, DateTimeOffset.UnixEpoch)));

        Assert.Equal(1_000, plan.Push.Count);
        Assert.Equal(1_000, plan.Pull.Count(c => c.Kind != PullKind.Converge));
        Assert.Empty(plan.Conflicts);
    }

    [Fact]
    public void StateFilesWriteAndReadInSeconds()
    {
        var (_, _, baseline) = Build(0, 0);
        var store = new SyncStateStore(_root, "c1");

        Timed("save baseline (100k units)", TimeSpan.FromSeconds(15), () => { store.SaveBaseline(baseline); return 0; });
        TestContext.Current.TestOutputHelper!.WriteLine($"baseline size: {new FileInfo(store.BaselinePath).Length / 1024 / 1024} MB");
        var loaded = Timed("load baseline (100k units)", TimeSpan.FromSeconds(15), () => store.LoadBaseline()!);

        Assert.Equal(Keys * s_languages.Length, loaded.Units.Count);
    }

    [Fact]
    public void DeliveryStateForTheWholeProjectIsComputedQuickly()
    {
        var (local, _, baseline) = Build(changedLocally: 500, 0);
        var states = Timed("delivery state (100k units)", TimeSpan.FromSeconds(10), () => SyncDelivery.Compute(baseline, new SyncJournal(), local));
        Assert.Equal(500, states.Count(s => s.Value == Toucan.Plugins.SourceDeliveryState.Pending));
    }

    [Fact]
    public async Task PushingAThousandChangesAgainstAHundredThousandUnitBaselineStaysFast()
    {
        var (local, _, baseline) = Build(0, 0);
        var store = new SyncStateStore(_root, "c1");
        store.SaveBaseline(baseline);
        var session = new ScriptedUnitSession();
        var changes = new List<PushChange>();
        for (var i = 0; i < 1_000; i++)
        {
            var unit = baseline.Units[i * 7];
            var remote = session.Put(unit.Remote.Name, unit.Language, unit.Value);
            changes.Add(new() { Kind = PushKind.Value, EntryId = unit.EntryId, Language = unit.Language, Remote = unit.Remote, Value = unit.Value + " new", ExpectedRevision = remote.Revision, BaseValue = unit.Value });
        }
        var connection = new SourceConnectionSettings { Id = "c1", Adapter = "x.y" };
        foreach (var l in s_languages) connection.Languages[l] = l;
        var plan = new SyncPlan("c1", Guid.NewGuid(), SyncPlanKind.Push, 1, DateTimeOffset.UnixEpoch, true, false, [], changes, [], []);

        var watch = Stopwatch.StartNew();
        var result = await new SyncExecutor().PushAsync(plan, connection, session, SourceCapabilities.ReadUnits | SourceCapabilities.WriteValues | SourceCapabilities.ConditionalWrites, new SourceLimits(MaxBatchSize: 100), store);
        watch.Stop();
        TestContext.Current.TestOutputHelper!.WriteLine($"push 1,000 changes in batches of 100: {watch.Elapsed.TotalMilliseconds:F0} ms");

        Assert.Equal(1_000, result.Confirmed);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"push took {watch.Elapsed.TotalSeconds:F1}s");
        Assert.NotNull(local);
    }
}
