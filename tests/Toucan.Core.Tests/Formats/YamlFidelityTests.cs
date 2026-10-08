using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>Regression tests for FMT-09 (YAML flat dotted keys, key/parent clash, YAML 1.1 scalars). Inputs are the repros from docs/known-bugs.md.</summary>
public sealed class YamlFidelityTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-yaml-" + Guid.NewGuid().ToString("N"));
    private readonly FileService _files = new(NullLogger<FileService>.Instance);

    public YamlFidelityTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static TranslationItem Item(string key, string value) => new() { Language = "en", Namespace = key, Value = value };

    private string Save(params TranslationItem[] items)
    {
        var context = new SaveContext
        {
            Languages = ["en"],
            LanguageDictionary = new Dictionary<string, IEnumerable<TranslationItem>> { ["en"] = items.ToList() },
            NsTreeItems = [],
        };
        new YamlSaveStrategy(_files).Save(_folder, context);
        return File.ReadAllText(Path.Combine(_folder, "en.yaml"));
    }

    private List<TranslationItem> Load() => new YamlLoadStrategy().Load(_folder).ToList();

    [Fact]
    public void FlatDottedFile_StaysFlat()
    {
        File.WriteAllText(Path.Combine(_folder, "en.yaml"), "\"a.b.c\": x\n\"app.title\": T\n");

        var items = Load();
        var text = Save([.. items]);

        Assert.Contains(items, i => i.Namespace == "a.b.c" && i.Value == "x");
        Assert.Contains("a.b.c: x", text);
        Assert.Contains("app.title: T", text);
        Assert.DoesNotContain("a:\n", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void NestedFile_StaysNested()
    {
        File.WriteAllText(Path.Combine(_folder, "en.yaml"), "a:\n  b:\n    c: x\n");

        var text = Save([.. Load()]).Replace("\r\n", "\n");

        Assert.Contains("a:\n  b:\n    c: x", text);
    }

    [Fact]
    public void KeyThatIsAlsoAParent_IsWrittenFlatWithoutSelf()
    {
        var text = Save(Item("app.title", "T"), Item("app", "Self"), Item("a.b.c", "x"));

        Assert.DoesNotContain("__self", text);
        Assert.Contains("app: Self", text);
        Assert.Contains("app.title: T", text);

        Directory.Delete(_folder, true);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "en.yaml"), text);
        var back = Load().ToDictionary(i => i.Namespace, i => i.Value);
        Assert.Equal("Self", back["app"]);
        Assert.Equal("T", back["app.title"]);
        Assert.Equal("x", back["a.b.c"]);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("~")]
    [InlineData("1.0")]
    [InlineData("007")]
    [InlineData("y")]
    public void SpecialScalars_AreQuotedAndRoundTrip(string value)
    {
        var text = Save(Item("k", value));

        Assert.Contains($"k: \"{value.Replace("\\", "\\\\")}\"", text);

        File.WriteAllText(Path.Combine(_folder, "en.yaml"), text);
        Assert.Equal(value, Assert.Single(Load()).Value);
    }
}
