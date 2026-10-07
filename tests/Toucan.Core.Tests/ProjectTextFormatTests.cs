using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Models;
using Toucan.Core.Tests.Formats;
using Toucan.Extensions;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public class ProjectTextFormatTests
{
    public static TheoryData<string> TextFormats()
    {
        var data = new TheoryData<string>();
        foreach (var strategy in FormatTestHost.SaveStrategies.Where(s => s.FormatId != FormatIds.JavaProperties))
            data.Add(strategy.FormatId);
        return data;
    }

    [Theory]
    [MemberData(nameof(TextFormats))]
    public void ProjectSave_AppliesPreferencesToFormatOutput(string format)
    {
        var folder = Directory.CreateTempSubdirectory("toucan-format-").FullName;
        try
        {
            var files = new FileService(NullLogger<FileService>.Instance);
            var service = new ProjectService(files, FormatTestHost.SaveStrategies);
            var project = service.CreateProject(folder, ["en"], format);
            project.TextEncoding = "UTF-8 BOM";
            project.LineEnding = "CRLF";
            var translations = new List<TranslationItem>
            {
                new() { Language = "en", Namespace = "app.title", Value = "Hello" },
                new() { Language = "en", Namespace = "app.bye", Value = "Goodbye" }
            };
            service.Save(project, translations.ToNsTree().ToList(), translations);
            foreach (var path in service.GetLanguageFiles(project, "en"))
            {
                Assert.True(File.Exists(path), path);
                Assert.True(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble), path);
                var text = File.ReadAllText(path);
                Assert.Contains("\r\n", text);
                Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void ProjectSave_FormatsPoFilesInTheirOriginalLocations()
    {
        var folder = Directory.CreateTempSubdirectory("toucan-po-format-").FullName;
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(folder, "de", "LC_MESSAGES")).FullName;
            var path = Path.Combine(nested, "messages.po");
            File.WriteAllText(path, "msgid \"Hello\"\nmsgstr \"Hallo\"\n");
            var service = new ProjectService(new FileService(NullLogger<FileService>.Instance), FormatTestHost.SaveStrategies,
                FormatTestHost.Factory, new ProjectModeResolver(), NullLogger<ProjectService>.Instance);
            var loaded = service.LoadProject(folder);
            loaded.Settings.TextEncoding = "UTF-8 BOM";
            loaded.Settings.LineEnding = "CRLF";
            service.Save(loaded.Settings, [], loaded.Translations);
            Assert.Equal(path, Assert.Single(service.GetLanguageFiles(loaded.Settings, "de")));
            Assert.True(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.DoesNotContain("\n", File.ReadAllText(path).Replace("\r\n", ""));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData("UTF-8", "LF")]
    [InlineData("UTF-8", "CRLF")]
    [InlineData("UTF-8 BOM", "LF")]
    [InlineData("UTF-8 BOM", "CRLF")]
    public void Apply_ConvertsMixedNewlinesAndPreservesUnicode(string encoding, string lineEnding)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Café\r\n日本語\nthree\rfour", Encoding.UTF8);
            ProjectTextFormat.Apply(path, encoding, lineEnding);
            var eol = lineEnding == "LF" ? "\n" : "\r\n";
            Assert.Equal(string.Join(eol, "Café", "日本語", "three", "four"), File.ReadAllText(path));
            Assert.Equal(encoding == "UTF-8 BOM", File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LineEndingOnly_PreservesLatin1ForJavaProperties()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "title=Café\r\n", Encoding.Latin1);
            ProjectTextFormat.Apply(path, null, "LF", Encoding.Latin1);
            Assert.Equal(Encoding.Latin1.GetBytes("title=Café\n"), File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineEndingOnly_PreservesUtf8BomChoice(bool bom)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Café\nsecond", new UTF8Encoding(bom));
            ProjectTextFormat.Apply(path, null, "CRLF");
            Assert.Equal(bom, File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
            Assert.Equal("Café\r\nsecond", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }
}
