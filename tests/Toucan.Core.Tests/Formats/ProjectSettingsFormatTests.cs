using System.Text.Json;
using Toucan.Core.Models;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// Pins how ProjectSettings persists the format (string "saveFormat", migrating legacy numeric "saveStyle") and the default file path
/// per format, so Phase 2 can replace the enum switch without changing output.
/// </summary>
public sealed class ProjectSettingsFormatTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "toucan-ps-" + Guid.NewGuid().ToString("N"));

    public ProjectSettingsFormatTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData(SaveStyles.Json, "en.json")]
    [InlineData(SaveStyles.Namespaced, "en.json")]
    [InlineData(SaveStyles.Yaml, "en.yaml")]
    [InlineData(SaveStyles.Toml, "en.toml")]
    [InlineData(SaveStyles.Resx, "Resources.en.resx")]
    [InlineData(SaveStyles.AndroidXml, "res/values-en/strings.xml")]
    [InlineData(SaveStyles.IosStrings, "en.lproj/Localizable.strings")]
    [InlineData(SaveStyles.Xliff, "en.xlf")]
    [InlineData(SaveStyles.Arb, "app_en.arb")]
    [InlineData(SaveStyles.Csv, "translations.csv")]
    [InlineData(SaveStyles.Properties, "en.po")]
    [InlineData(SaveStyles.Adb, "en.ini")]
    [InlineData(SaveStyles.JavaProperties, "en.properties")]
    [InlineData(SaveStyles.LaravelPhp, "en/messages.php")]
    public void SaveWritesDefaultPathForStyle(SaveStyles style, string expectedPath)
    {
        var settings = new ProjectSettings { ProjectPath = _folder, Languages = ["en"], SaveStyle = style };
        settings.Save();

        var reloaded = ProjectSettings.LoadFrom(_folder)!;
        Assert.Equal(expectedPath, reloaded.TranslationPackages[0].TranslationUrls[0].Path);
    }

    [Fact]
    public void FormatIsPersistedAsStringId()
    {
        new ProjectSettings { ProjectPath = _folder, Languages = ["en"], SaveStyle = SaveStyles.AndroidXml }.Save();

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "toucan.tproj")));
        Assert.Equal("android-xml", doc.RootElement.GetProperty("saveFormat").GetString());
        Assert.False(doc.RootElement.TryGetProperty("saveStyle", out _), "legacy key must not be written back");
    }

    [Theory]
    [MemberData(nameof(FormatRoundTripTests.AllStyles), MemberType = typeof(FormatRoundTripTests))]
    public void SaveStyleRoundTripsThroughProjectFile(SaveStyles style)
    {
        new ProjectSettings { ProjectPath = _folder, Languages = ["en"], SaveStyle = style }.Save();
        Assert.Equal(style, ProjectSettings.LoadFrom(_folder)!.SaveStyle);
    }

    [Theory]
    [MemberData(nameof(FormatRoundTripTests.AllStyles), MemberType = typeof(FormatRoundTripTests))]
    public void LegacyNumericSaveStyleMigratesToFormatId(SaveStyles style)
    {
        File.WriteAllText(Path.Combine(_folder, "toucan.tproj"), $$"""{ "languages": ["en"], "saveStyle": {{(int)style}} }""");

        var loaded = ProjectSettings.LoadFrom(_folder)!;
        Assert.Equal(FormatIds.FromStyle(style), loaded.SaveFormat);
        Assert.Equal(style, loaded.SaveStyle);
    }

    [Fact]
    public void LegacyEnumNameSaveStyleMigrates()
    {
        File.WriteAllText(Path.Combine(_folder, "toucan.tproj"), """{ "saveStyle": "AndroidXml" }""");
        Assert.Equal(FormatIds.AndroidXml, ProjectSettings.LoadFrom(_folder)!.SaveFormat);
    }

    [Fact]
    public void SaveFormatWinsOverStaleLegacySaveStyle()
    {
        File.WriteAllText(Path.Combine(_folder, "toucan.tproj"), """{ "saveFormat": "arb", "saveStyle": 0 }""");
        Assert.Equal(FormatIds.Arb, ProjectSettings.LoadFrom(_folder)!.SaveFormat);
    }

    [Fact]
    public void MigratedProjectIsRewrittenWithFormatOnly()
    {
        File.WriteAllText(Path.Combine(_folder, "toucan.tproj"), """{ "languages": ["en"], "saveStyle": 9 }""");

        ProjectSettings.LoadFrom(_folder)!.Save();

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "toucan.tproj")));
        Assert.Equal("arb", doc.RootElement.GetProperty("saveFormat").GetString());
        Assert.False(doc.RootElement.TryGetProperty("saveStyle", out _));
    }

    [Fact]
    public void UnknownFormatIdIsPreservedThroughLoadAndSave()
    {
        // A plugin format must survive a load/save cycle instead of being dropped or coerced to JSON.
        File.WriteAllText(Path.Combine(_folder, "toucan.tproj"), """{ "languages": ["en"], "saveFormat": "my-plugin-format" }""");

        var loaded = ProjectSettings.LoadFrom(_folder)!;
        Assert.Equal("my-plugin-format", loaded.SaveFormat);

        loaded.Save();
        Assert.Equal("my-plugin-format", ProjectSettings.LoadFrom(_folder)!.SaveFormat);
    }

    [Fact]
    public void ProjectWithoutFormatDefaultsToJson()
    {
        File.WriteAllText(Path.Combine(_folder, "toucan.tproj"), """{ "languages": ["en"] }""");
        Assert.Equal(FormatIds.Json, ProjectSettings.LoadFrom(_folder)!.SaveFormat);
    }
}

public class FormatIdsTests
{
    [Theory]
    [MemberData(nameof(FormatRoundTripTests.AllStyles), MemberType = typeof(FormatRoundTripTests))]
    public void EveryStyleMapsToAUniqueIdAndBack(SaveStyles style)
    {
        var id = FormatIds.FromStyle(style);
        Assert.True(FormatIds.TryGetStyle(id, out var back));
        Assert.Equal(style, back);
    }

    [Fact]
    public void IdsAreUnique()
    {
        var ids = Enum.GetValues<SaveStyles>().Select(FormatIds.FromStyle).ToList();
        Assert.Equal(ids.Count, ids.Distinct(FormatIds.Comparer).Count());
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        Assert.True(FormatIds.TryGetStyle("ANDROID-XML", out var style));
        Assert.Equal(SaveStyles.AndroidXml, style);
        Assert.NotNull(FormatTestHost.Factory.GetSaveStrategy("Android-Xml"));
    }

    [Fact]
    public void UnknownIdIsNotAStyle()
    {
        Assert.False(FormatIds.TryGetStyle("my-plugin-format", out _));
        Assert.False(FormatIds.TryGetStyle(null, out _));
        Assert.Null(FormatTestHost.Factory.GetSaveStrategy("my-plugin-format"));
        Assert.Null(FormatTestHost.Factory.GetLoadStrategy("my-plugin-format"));
    }
}
