using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>MIG-01: merging the copies of language files that 0.23 wrote at the project root.</summary>
public class LegacyCopyMergeTests
{
    private static Toucan.Core.Contracts.Services.ISaveStrategy Yaml => Formats.FormatTestHost.Factory.GetSaveStrategy(FormatIds.Yaml)!;

    private static TranslationItem Item(string file, string key, string value, string language = "en") =>
        new() { Language = language, Namespace = key, Value = value, FormatData = new() { ["yaml.file"] = file } };

    [Fact]
    public void NoCopiesMeansNothingToMerge() =>
        Assert.Null(LegacyCopyMerge.Apply(Yaml, [Item("config/locales/en.yml", "a", "1")]));

    [Fact]
    public void AnEditedValueInTheCopyWinsAndKeysOnlyInTheCopyMoveToTheOriginal()
    {
        var result = LegacyCopyMerge.Apply(Yaml,
        [
            Item("config/locales/en.yml", "same", "x"), Item("en.yaml", "same", "x"),
            Item("config/locales/en.yml", "edited", "old"), Item("en.yaml", "edited", "new"), Item("en.yaml", "edited", "old"),
            Item("en.yaml", "added", "fresh"),
        ])!;

        Assert.Equal(["en.yaml"], result.Copies);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal("new", result.Items.Single(i => i.Namespace == "edited").Value);
        Assert.Equal("config/locales/en.yml", result.Items.Single(i => i.Namespace == "added").FormatData!["yaml.file"]);
        Assert.All(result.Items, i => Assert.Equal("config/locales/en.yml", i.FormatData!["yaml.file"]));
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public void ALanguageWithTwoOriginalFilesIsNotGuessed() =>
        Assert.Null(LegacyCopyMerge.Apply(Yaml,
            [Item("config/locales/en.yml", "a", "1"), Item("config/locales/admin.en.yml", "b", "2"), Item("en.yaml", "a", "3")]));

    [Fact]
    public void TheCopiesMoveToATimestampedBackup()
    {
        var root = Directory.CreateTempSubdirectory("toucan-copies-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "en.yaml"), "en:\n  a: 1\n");
            var backup = LegacyCopyMerge.MoveCopiesToBackup(root, ["en.yaml", "missing.yaml"], new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero))!;
            Assert.False(File.Exists(Path.Combine(root, "en.yaml")));
            Assert.Equal("en:\n  a: 1\n", File.ReadAllText(Path.Combine(backup, "en.yaml")));
            Assert.EndsWith(Path.Combine(".toucan", "backup", "0.23-copies", "20260102-030405"), backup);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
