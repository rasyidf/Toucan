using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Xunit;

namespace Toucan.Core.Tests;

public class CommentPersistenceServiceTests : IDisposable
{
    private readonly CommentPersistenceService _service;
    private readonly string _tempFolder;

    public CommentPersistenceServiceTests()
    {
        _service = new CommentPersistenceService(NullLogger<CommentPersistenceService>.Instance);
        _tempFolder = Path.Combine(Path.GetTempPath(), "toucan-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, recursive: true);
    }

    #region RequiresSidecar

    [Theory]
    [InlineData(SaveStyles.Json, true)]
    [InlineData(SaveStyles.Namespaced, true)]
    [InlineData(SaveStyles.Yaml, true)]
    [InlineData(SaveStyles.Adb, true)]
    [InlineData(SaveStyles.Toml, true)]
    [InlineData(SaveStyles.IosStrings, true)]
    [InlineData(SaveStyles.Arb, true)]
    [InlineData(SaveStyles.Csv, true)]
    [InlineData(SaveStyles.JavaProperties, true)]
    [InlineData(SaveStyles.LaravelPhp, true)]
    [InlineData(SaveStyles.Xliff, false)]
    [InlineData(SaveStyles.Resx, false)]
    [InlineData(SaveStyles.Properties, false)]
    [InlineData(SaveStyles.AndroidXml, false)]
    public void RequiresSidecar_ReturnsExpected(SaveStyles style, bool expected)
    {
        Assert.Equal(expected, _service.RequiresSidecar(style));
    }

    #endregion

    #region SaveComments

    [Fact]
    public void SaveComments_WritesSidecarForJsonFormat()
    {
        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "greeting.hello", Value = "Hello", Comment = "Formal greeting" },
            new() { Language = "en", Namespace = "greeting.bye", Value = "Bye", Comment = "Farewell" },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Json, translations);

        var sidecarPath = Path.Combine(_tempFolder, "en.json.comments.json");
        Assert.True(File.Exists(sidecarPath));

        var json = File.ReadAllText(sidecarPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        var comments = root.GetProperty("comments");
        Assert.Equal("Formal greeting", comments.GetProperty("greeting.hello").GetString());
        Assert.Equal("Farewell", comments.GetProperty("greeting.bye").GetString());
    }

    [Fact]
    public void SaveComments_ExcludesEmptyComments()
    {
        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "v1", Comment = "Has comment" },
            new() { Language = "en", Namespace = "key2", Value = "v2", Comment = "" },
            new() { Language = "en", Namespace = "key3", Value = "v3", Comment = null! },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Json, translations);

        var sidecarPath = Path.Combine(_tempFolder, "en.json.comments.json");
        var json = File.ReadAllText(sidecarPath);
        var doc = JsonDocument.Parse(json);
        var comments = doc.RootElement.GetProperty("comments");

        Assert.Equal("Has comment", comments.GetProperty("key1").GetString());
        Assert.False(comments.TryGetProperty("key2", out _));
        Assert.False(comments.TryGetProperty("key3", out _));
    }

    [Fact]
    public void SaveComments_TruncatesCommentsOver2000Chars()
    {
        var longComment = new string('x', 3000);
        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "v1", Comment = longComment },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Json, translations);

        var sidecarPath = Path.Combine(_tempFolder, "en.json.comments.json");
        var json = File.ReadAllText(sidecarPath);
        var doc = JsonDocument.Parse(json);
        var savedComment = doc.RootElement.GetProperty("comments").GetProperty("key1").GetString();

        Assert.Equal(2000, savedComment!.Length);
        Assert.Equal(new string('x', 2000), savedComment);
    }

    [Fact]
    public void SaveComments_WritesOneSidecarPerLanguage()
    {
        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "Hello", Comment = "English note" },
            new() { Language = "fr", Namespace = "key1", Value = "Bonjour", Comment = "French note" },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Json, translations);

        Assert.True(File.Exists(Path.Combine(_tempFolder, "en.json.comments.json")));
        Assert.True(File.Exists(Path.Combine(_tempFolder, "fr.json.comments.json")));
    }

    [Fact]
    public void SaveComments_DeletesSidecarWhenAllCommentsEmpty()
    {
        // First write a sidecar
        var sidecarPath = Path.Combine(_tempFolder, "en.json.comments.json");
        File.WriteAllText(sidecarPath, """{"schemaVersion":"1.0","comments":{"key1":"old"}}""");

        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "v1", Comment = "" },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Json, translations);

        Assert.False(File.Exists(sidecarPath));
    }

    [Fact]
    public void SaveComments_DoesNothingForInlineFormats()
    {
        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "Hello", Comment = "A note" },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Xliff, translations);

        // No sidecar file should be created
        var files = Directory.GetFiles(_tempFolder, "*.comments.json", SearchOption.AllDirectories);
        Assert.Empty(files);
    }

    [Fact]
    public void SaveComments_YamlFormatUseCorrectFilename()
    {
        var translations = new List<TranslationItem>
        {
            new() { Language = "de", Namespace = "key1", Value = "Hallo", Comment = "German" },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Yaml, translations);

        Assert.True(File.Exists(Path.Combine(_tempFolder, "de.yaml.comments.json")));
    }

    [Fact]
    public void SaveComments_ArbFormatUsesCorrectFilename()
    {
        var translations = new List<TranslationItem>
        {
            new() { Language = "es", Namespace = "key1", Value = "Hola", Comment = "Spanish" },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Arb, translations);

        Assert.True(File.Exists(Path.Combine(_tempFolder, "app_es.arb.comments.json")));
    }

    #endregion

    #region LoadComments

    [Fact]
    public void LoadComments_RestoresCommentsFromSidecar()
    {
        // Write sidecar manually
        var sidecarPath = Path.Combine(_tempFolder, "en.json.comments.json");
        File.WriteAllText(sidecarPath, """
        {
            "schemaVersion": "1.0",
            "comments": {
                "greeting.hello": "Formal greeting",
                "greeting.bye": "Farewell"
            }
        }
        """);

        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "greeting.hello", Value = "Hello" },
            new() { Language = "en", Namespace = "greeting.bye", Value = "Bye" },
        };

        _service.LoadComments(_tempFolder, SaveStyles.Json, translations);

        Assert.Equal("Formal greeting", translations[0].Comment);
        Assert.Equal("Farewell", translations[1].Comment);
    }

    [Fact]
    public void LoadComments_DiscardsOrphanedEntries()
    {
        var sidecarPath = Path.Combine(_tempFolder, "en.json.comments.json");
        File.WriteAllText(sidecarPath, """
        {
            "schemaVersion": "1.0",
            "comments": {
                "existing.key": "Valid comment",
                "orphan.key": "Should be discarded"
            }
        }
        """);

        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "existing.key", Value = "Hello" },
        };

        _service.LoadComments(_tempFolder, SaveStyles.Json, translations);

        Assert.Equal("Valid comment", translations[0].Comment);
        // Orphan.key is simply not loaded — only existing items get comments
    }

    [Fact]
    public void LoadComments_DoesNothingForInlineFormats()
    {
        // Write sidecar that should be ignored
        File.WriteAllText(
            Path.Combine(_tempFolder, "en.xlf.comments.json"),
            """{"schemaVersion":"1.0","comments":{"key1":"note"}}""");

        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "Hello" },
        };

        _service.LoadComments(_tempFolder, SaveStyles.Xliff, translations);

        // Comment should remain empty since Xliff is inline format
        Assert.Equal(string.Empty, translations[0].Comment);
    }

    [Fact]
    public void LoadComments_HandlesMissingSidecarGracefully()
    {
        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "Hello" },
        };

        // No sidecar exists — should not throw
        _service.LoadComments(_tempFolder, SaveStyles.Json, translations);

        Assert.Equal(string.Empty, translations[0].Comment);
    }

    [Fact]
    public void LoadComments_HandlesMalformedSidecarGracefully()
    {
        var sidecarPath = Path.Combine(_tempFolder, "en.json.comments.json");
        File.WriteAllText(sidecarPath, "not valid json {{{");

        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "Hello" },
        };

        // Should not throw
        _service.LoadComments(_tempFolder, SaveStyles.Json, translations);

        Assert.Equal(string.Empty, translations[0].Comment);
    }

    [Fact]
    public void LoadComments_HandlesEmptyCommentsSection()
    {
        var sidecarPath = Path.Combine(_tempFolder, "en.json.comments.json");
        File.WriteAllText(sidecarPath, """{"schemaVersion":"1.0"}""");

        var translations = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "Hello" },
        };

        _service.LoadComments(_tempFolder, SaveStyles.Json, translations);

        Assert.Equal(string.Empty, translations[0].Comment);
    }

    #endregion

    #region Round-trip

    [Fact]
    public void SaveThenLoad_RoundTripsComments()
    {
        var original = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "app.title", Value = "My App", Comment = "App title" },
            new() { Language = "en", Namespace = "app.desc", Value = "Description", Comment = "App description" },
            new() { Language = "fr", Namespace = "app.title", Value = "Mon App", Comment = "Titre de l'app" },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Json, original);

        // Create fresh items without comments
        var loaded = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "app.title", Value = "My App" },
            new() { Language = "en", Namespace = "app.desc", Value = "Description" },
            new() { Language = "fr", Namespace = "app.title", Value = "Mon App" },
        };

        _service.LoadComments(_tempFolder, SaveStyles.Json, loaded);

        Assert.Equal("App title", loaded[0].Comment);
        Assert.Equal("App description", loaded[1].Comment);
        Assert.Equal("Titre de l'app", loaded[2].Comment);
    }

    [Fact]
    public void SaveThenLoad_TruncatedCommentRoundTrips()
    {
        var longComment = new string('a', 2500);
        var original = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "v1", Comment = longComment },
        };

        _service.SaveComments(_tempFolder, SaveStyles.Json, original);

        var loaded = new List<TranslationItem>
        {
            new() { Language = "en", Namespace = "key1", Value = "v1" },
        };

        _service.LoadComments(_tempFolder, SaveStyles.Json, loaded);

        Assert.Equal(2000, loaded[0].Comment.Length);
        Assert.Equal(new string('a', 2000), loaded[0].Comment);
    }

    #endregion
}
