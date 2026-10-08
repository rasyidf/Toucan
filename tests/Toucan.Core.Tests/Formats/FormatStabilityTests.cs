using Toucan.Core.Models;
using Toucan.Extensions;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// Saving must be stable: the same data gives the same files, a save → load → save cycle changes nothing, and locale
/// variants keep their identity. Together they mean opening a project and saving it again produces no diff.
/// </summary>
public sealed class FormatStabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toucan-stab-" + Guid.NewGuid().ToString("N"));

    public FormatStabilityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    public static TheoryData<string> LoadableFormats()
    {
        var data = new TheoryData<string>();
        foreach (var s in FormatTestHost.SaveStrategies.Where(s => FormatTestHost.Factory.GetLoadStrategy(s.FormatId) != null))
            data.Add(s.FormatId);
        return data;
    }

    private static List<TranslationItem> Sample(params string[] languages) =>
    [
        .. languages.SelectMany(l => new[]
        {
            new TranslationItem { Language = l, Namespace = "app.title", Value = $"Title {l}" },
            new TranslationItem { Language = l, Namespace = "app.multi", Value = $"Line one {l}\nLine two" },
            new TranslationItem { Language = l, Namespace = "buttons.save", Value = $"Save {l}" },
            new TranslationItem { Language = l, Namespace = "buttons.cancel", Value = $"Cancel {l}" },
        }),
    ];

    private static SaveContext Context(List<TranslationItem> items) => new()
    {
        LanguageDictionary = items.GroupBy(i => i.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
        NsTreeItems = items.ToNsTree().ToList(),
        Languages = [.. items.Select(i => i.Language).Distinct()],
    };

    private string NewFolder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), f => File.ReadAllText(f).ReplaceLineEndings("\n"));

    private static List<TranslationItem> Roundtrip(string formatId, string folder) =>
        [.. FormatTestHost.Factory.GetLoadStrategy(formatId)!.Load(folder)];

    [Theory]
    [MemberData(nameof(LoadableFormats))]
    public void SavingTheSameDataTwice_GivesIdenticalFiles(string formatId)
    {
        var items = Sample("en", "fr");
        var strategy = FormatTestHost.Factory.GetSaveStrategy(formatId)!;
        var first = NewFolder("first");
        var second = NewFolder("second");

        strategy.Save(first, Context(items));
        strategy.Save(second, Context(items));

        Assert.Equal(Snapshot(first), Snapshot(second));
    }

    [Theory]
    [MemberData(nameof(LoadableFormats))]
    public void SaveLoadSave_ChangesNothing_AndCreatesNoNewFiles(string formatId)
    {
        var strategy = FormatTestHost.Factory.GetSaveStrategy(formatId)!;
        var folder = NewFolder("cycle");
        strategy.Save(folder, Context(Sample("en", "fr")));
        var before = Snapshot(folder);

        var loaded = Roundtrip(formatId, folder);
        strategy.Save(folder, Context(loaded));

        Assert.Equal(before, Snapshot(folder));
    }

    [Theory]
    [MemberData(nameof(LoadableFormats))]
    public void LocaleVariants_KeepTheirIdentity(string formatId)
    {
        var strategy = FormatTestHost.Factory.GetSaveStrategy(formatId)!;
        var folder = NewFolder("locales");
        var items = Sample("en", "pt-BR", "zh-Hans");
        strategy.Save(folder, Context(items));

        var languages = Roundtrip(formatId, folder).Select(i => Normalize(i.Language)).Distinct().Order().ToList();

        Assert.Equal(["en", "pt-br", "zh-hans"], languages);
    }

    /// <summary>Formats write locales their own way (pt_BR, values-pt-rBR); compare on the BCP 47 form.</summary>
    private static string Normalize(string language) =>
        language.Replace('_', '-').Replace("-r", "-", StringComparison.Ordinal).ToLowerInvariant();
}
