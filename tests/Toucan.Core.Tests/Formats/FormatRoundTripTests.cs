using Toucan.Core.Models;
using Toucan.Extensions;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// Characterization tests for the built-in formats, written before the FormatId migration
/// (docs/archive/plugin-system-plan.md, Phase 2). They pin current save -> load behavior so the
/// refactor can be verified as behavior-preserving.
/// </summary>
public sealed class FormatRoundTripTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-fmt-" + Guid.NewGuid().ToString("N"));

    public FormatRoundTripTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    /// <summary>Every enum value must have a save strategy; this is the set Phase 2 must keep intact.</summary>
    public static TheoryData<SaveStyles> AllStyles()
    {
        var data = new TheoryData<SaveStyles>();
        foreach (var s in Enum.GetValues<SaveStyles>()) data.Add(s);
        return data;
    }

    /// <summary>Styles that have a dedicated load strategy (Adb/INI is save-only today).</summary>
    public static TheoryData<SaveStyles> RoundTrippableStyles()
    {
        var data = new TheoryData<SaveStyles>();
        foreach (var s in Enum.GetValues<SaveStyles>().Where(s => s != SaveStyles.Adb)) data.Add(s);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllStyles))]
    public void EveryStyleHasExactlyOneSaveStrategy(SaveStyles style)
    {
        Assert.Single(FormatTestHost.SaveStrategies, s => s.FormatId == FormatIds.FromStyle(style));
        Assert.NotNull(FormatTestHost.Factory.GetSaveStrategy(FormatIds.FromStyle(style)));
    }

    [Fact]
    public void AdbIsSaveOnlyToday()
    {
        Assert.Null(FormatTestHost.Factory.GetLoadStrategy(FormatIds.Ini));
    }

    [Theory]
    [MemberData(nameof(RoundTrippableStyles))]
    public void SaveThenLoadPreservesKeysAndValues(SaveStyles style)
    {
        var items = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "app.title", Value = "Hello" },
            new() { Language = "en", Namespace = "app.bye", Value = "Goodbye" },
            new() { Language = "fr", Namespace = "app.title", Value = "Bonjour" },
            new() { Language = "fr", Namespace = "app.bye", Value = "Au revoir" },
        };
        var context = new SaveContext
        {
            LanguageDictionary = items.GroupBy(i => i.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
            // Namespaced saves from the tree (as ProjectService does), so build it for every format.
            NsTreeItems = items.ToNsTree().ToList(),
            Languages = ["en", "fr"],
        };

        FormatTestHost.Factory.GetSaveStrategy(FormatIds.FromStyle(style))!.Save(_folder, context);
        var loaded = FormatTestHost.Factory.GetLoadStrategy(FormatIds.FromStyle(style))!.Load(_folder).ToList();

        var actual = loaded.Where(i => !string.IsNullOrEmpty(i.Value))
            .Select(i => (Lang(i.Language), i.Namespace, i.Value)).Distinct().OrderBy(t => t).ToList();
        var expected = items.Select(i => (i.Language, i.Namespace, i.Value)).OrderBy(t => t).ToList();
        Assert.Equal(expected, actual);
    }

    private List<TranslationItem> SaveNamespacedAndReload(params TranslationItem[] items)
    {
        var context = new SaveContext
        {
            LanguageDictionary = new() { ["en"] = [.. items] },
            NsTreeItems = items.ToList().ToNsTree().ToList(),
            Languages = ["en"],
        };
        FormatTestHost.Factory.GetSaveStrategy(FormatIds.Namespaced)!.Save(_folder, context);
        return FormatTestHost.Factory.GetLoadStrategy(FormatIds.Namespaced)!.Load(_folder).ToList();
    }

    [Fact]
    public void NamespacedSaveWritesEachKeyOnceSoAReloadSeesItOnce()
    {
        var loaded = SaveNamespacedAndReload(
            new() { Language = "en", Namespace = "app.title", Value = "Hello" },
            new() { Language = "en", Namespace = "greeting", Value = "Hi" });

        Assert.Single(loaded, i => i.Namespace == "app.title" && i.Value == "Hello");
        Assert.Single(loaded, i => i.Namespace == "greeting" && i.Value == "Hi");
        Assert.True(File.Exists(Path.Combine(_folder, "locales", "en", "app.json")));
        Assert.DoesNotContain("app", File.ReadAllText(Path.Combine(_folder, "en.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOlderMergedFileIsRewrittenWithoutTheNamespacedCopies()
    {
        // Saved by an older Toucan: the merged file repeats what locales/en/app.json holds.
        File.WriteAllText(Path.Combine(_folder, "en.json"), """{"app":{"title":"Hello"}}""");
        Directory.CreateDirectory(Path.Combine(_folder, "locales", "en"));
        File.WriteAllText(Path.Combine(_folder, "locales", "en", "app.json"), """{"title":"Hello"}""");

        var loaded = SaveNamespacedAndReload(new TranslationItem { Language = "en", Namespace = "app.title", Value = "Hello again" });

        Assert.Equal("Hello again", Assert.Single(loaded, i => i.Namespace == "app.title").Value);
    }

    // Some formats encode locale as en_US / values-en; compare on the primary subtag only.
    private static string Lang(string language) => language.Split('-', '_')[0].ToLowerInvariant();
}
