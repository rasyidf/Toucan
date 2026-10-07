using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>Regression tests for the bugs fixed in 0.17.1 that had none (QA-01 in docs/known-bugs.md).</summary>
public sealed class Qa01RegressionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-qa01-" + Guid.NewGuid().ToString("N"));

    public Qa01RegressionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static TranslationItem Item(string lang, string ns, string value) => new() { Language = lang, Namespace = ns, Value = value };

    // ---- B1: DiffMergeEngine merged items stayed dirty

    [Fact]
    public void DiffMerge_MergedItemsAreNotDirtyAfterwards()
    {
        using var service = new TranslationManagementService(Substitute.For<IUndoRedoService>());
        var baseItems = new List<TranslationItem> { Item("en", "a", "1"), Item("en", "gone", "x") };
        service.Initialize(baseItems);
        var theirs = new List<TranslationItem> { Item("en", "a", "2"), Item("en", "new", "n") };

        var engine = new DiffMergeEngine();
        var diff = engine.ComputeDiff([Item("en", "a", "1"), Item("en", "gone", "x")], [.. service.Translations], theirs);
        var result = engine.ApplyNonConflicting(diff, service);

        Assert.Equal(3, result.AutoApplied); // modified, added, deleted
        Assert.Empty(result.Conflicts);
        Assert.False(service.IsDirty, "values now equal the files on disk, so nothing is unsaved");
        Assert.Empty(service.GetDirtyItems());
        Assert.Equal("2", service.Translations.Single(t => t.Namespace == "a").Value);
        Assert.Contains(service.Translations, t => t.Namespace == "new" && t.Value == "n");
        Assert.DoesNotContain(service.Translations, t => t.Namespace == "gone");
    }

    [Fact]
    public void DiffMerge_ConflictsAreReportedAndLeftAlone()
    {
        using var service = new TranslationManagementService(Substitute.For<IUndoRedoService>());
        service.Initialize([Item("en", "a", "1")]);
        service.Translations[0].Value = "mine";
        var engine = new DiffMergeEngine();

        var diff = engine.ComputeDiff([Item("en", "a", "1")], [.. service.Translations], [Item("en", "a", "theirs")]);
        var result = engine.ApplyNonConflicting(diff, service);

        Assert.Single(result.Conflicts);
        Assert.Equal("mine", service.Translations[0].Value);
        Assert.True(service.IsDirty);
    }

    // ---- B3: double DirtyStateChanged (TOCTOU)

    [Fact]
    public void DirtyStateChanged_FiresOncePerTransition_UnderConcurrentChecks()
    {
        using var service = new TranslationManagementService(Substitute.For<IUndoRedoService>());
        var item = Item("en", "a", "1");
        service.Initialize([item]);
        var events = new List<bool>();
        service.DirtyStateChanged += (_, dirty) => { lock (events) events.Add(dirty); };

        item.Value = "changed";
        Parallel.For(0, 64, _ => service.AddItems([]));
        Assert.Equal([true], events);

        Parallel.For(0, 64, _ => service.MarkAllSaved());
        Assert.Equal([true, false], events);
    }

    // ---- B2: AutoSaveService crash when disposed during a save

    [Fact]
    public async Task AutoSave_DisposedWhileSaving_DoesNotCrash()
    {
        var saving = new TaskCompletionSource<ProjectSaveResult>();
        var started = new TaskCompletionSource();
        var lifecycle = Substitute.For<IProjectLifecycleService>();
        lifecycle.SaveProjectAsync(Arg.Any<CancellationToken>()).Returns(_ => { started.TrySetResult(); return saving.Task; });
        var management = Substitute.For<ITranslationManagementService>();
        management.IsDirty.Returns(true);
        var failures = new List<string>();
        var service = new AutoSaveService(new Lazy<IProjectLifecycleService>(() => lifecycle), management, Substitute.For<IFileWatcherService>(), NullLogger<AutoSaveService>.Instance);
        service.AutoSaveFailed += (_, message) => failures.Add(message);

        // Run one tick without waiting the minimum 10 s interval. The tick is async void, so an exception escaping it
        // is posted to the SynchronizationContext it started on; record those instead of letting the runtime swallow them.
        var escaped = new EscapedExceptions();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(escaped);
        try
        {
            service.Start(TimeSpan.FromSeconds(10));
            typeof(AutoSaveService).GetMethod("OnTimerTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(service, [null]);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        service.Dispose();
        saving.SetResult(new ProjectSaveResult(ProjectSaveStatus.Success));
        await Task.Delay(200); // let the async-void tick finish; an unhandled exception would fail the run

        Assert.Empty(escaped.Exceptions);
        Assert.Empty(failures);
        Assert.False(service.IsEnabled);
    }

    private sealed class EscapedExceptions : SynchronizationContext
    {
        public List<Exception> Exceptions { get; } = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            try { d(state); }
            catch (Exception ex) { lock (Exceptions) Exceptions.Add(ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex); }
        }
    }

    // ---- B5: iOS .strings literal \n corrupted

    [Theory]
    [InlineData("literal \\n stays two characters")]
    [InlineData("real\nnewline")]
    [InlineData("quote \" and backslash \\")]
    public void IosStrings_EscapesRoundTrip(string value)
    {
        var files = new FileService(NullLogger<FileService>.Instance);
        var items = new List<TranslationItem> { Item("en", "k", value) };
        var context = new SaveContext
        {
            Languages = ["en"],
            LanguageDictionary = new Dictionary<string, IEnumerable<TranslationItem>> { ["en"] = items },
            NsTreeItems = [],
        };

        new IosStringsSaveStrategy(files).Save(_folder, context);
        var loaded = new IosStringsLoadStrategy().Load(_folder).Single(i => i.Namespace == "k");

        Assert.Equal(value, loaded.Value);
    }

    // ---- B6: Java .properties line continuations truncated values

    [Fact]
    public void JavaProperties_LineContinuationsJoinTheValue()
    {
        File.WriteAllText(Path.Combine(_folder, "en.properties"),
            "greeting=Hello, \\\n    wide \\\n    world\nescaped=ends with a backslash \\\\\nnext=ok\n", System.Text.Encoding.Latin1);

        var loaded = new JavaPropertiesLoadStrategy().Load(_folder).ToDictionary(i => i.Namespace, i => i.Value);

        Assert.Equal("Hello, wide world", loaded["greeting"]);
        Assert.Equal("ends with a backslash \\", loaded["escaped"]);
        Assert.Equal("ok", loaded["next"]);
    }
}
