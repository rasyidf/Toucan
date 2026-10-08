using Toucan.Core.Models;
using Toucan.Extensions;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// Value fidelity for every format that can be loaded: text that is awkward to escape must come back from
/// save → load exactly. One case per construct so a failure names the format and the construct.
/// </summary>
public sealed class FormatFidelityFixtureTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-fid-" + Guid.NewGuid().ToString("N"));

    public FormatFidelityFixtureTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    public static readonly (string Name, string Value)[] Constructs =
    [
        ("multiline", "line1\nline2"),
        ("crlf", "line1\r\nline2"),
        ("quotes", "He said \"hi\" and 'bye'"),
        ("backslash", "C:\\dir\\file"),
        ("literal-escape", "path\\new\\table and \\u0041 and \\\\"),
        ("trailing-backslash", "ends with \\"),
        ("unicode", "日本語 ñ é 😀"),
        ("placeholders", "{0} %s %d {name} %1$s :count"),
        ("markup", "<b>bold</b> & more"),
        ("separators", "a=b: c; d # e"),
        ("leadingspace", " lead and trail "),
        ("tab", "a\tb"),
        ("percent", "100% sure"),
        ("dollar", "$var and ${x}"),
        ("yamlwords", "yes"),
        ("number", "1.0"),
    ];

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var strategy in FormatTestHost.SaveStrategies.Where(s => FormatTestHost.Factory.GetLoadStrategy(s.FormatId) != null))
            foreach (var (name, _) in Constructs)
                data.Add(strategy.FormatId, name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ValueSurvivesSaveAndLoad(string formatId, string construct)
    {
        var value = Constructs.Single(c => c.Name == construct).Value;
        var items = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "app." + construct, Value = value },
            new() { Language = "fr", Namespace = "app." + construct, Value = value },
        };
        var context = new SaveContext
        {
            LanguageDictionary = items.GroupBy(i => i.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
            NsTreeItems = items.ToNsTree().ToList(),
            Languages = ["en", "fr"],
        };

        FormatTestHost.Factory.GetSaveStrategy(formatId)!.Save(_folder, context);
        var back = FormatTestHost.Factory.GetLoadStrategy(formatId)!.Load(_folder)
            .Where(i => i.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase) && i.Namespace.EndsWith(construct, StringComparison.Ordinal))
            .Select(i => i.Value).Distinct().ToList();

        Assert.Equal(value, Assert.Single(back));
    }
}
