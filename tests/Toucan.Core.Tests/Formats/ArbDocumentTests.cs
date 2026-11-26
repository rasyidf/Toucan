using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services.LoadStrategies;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// Document saving for ARB (V1-11): only what changed is written, comments stay in <c>@key.description</c>, and each message's
/// <c>@key</c> block (placeholders and unknown fields) stays with it.
/// </summary>
public class ArbDocumentTests
{
    private const string Sample = """
        {
            "@@locale": "en",
            "@@x-generated": true,
            "greeting": "Hello {name}",
            "@greeting": {
                "description": "Shown on the home screen",
                "placeholders": { "name": { "type": "String", "example": "Ada", "x-custom": { "keep": true } } },
                "x-owner": "growth"
            },
            "bye":   "Goodbye",
            "count": 3,
            "plain": "No metadata"
        }

        """;

    private static IDocumentSaveStrategy Writer => (IDocumentSaveStrategy)FormatTestHost.Factory.GetSaveStrategy(FormatIds.Arb)!;
    private static IDocumentLoadStrategy Reader => (IDocumentLoadStrategy)FormatTestHost.Factory.GetLoadStrategy(FormatIds.Arb)!;

    private static TranslationPath P(string key) => new([key]);

    private static DocumentTranslation Current(string content, string key) => Reader.LoadDocument(content).Single(t => t.KeyPath.Equals(P(key)));

    private static string Save(params DocumentTranslationEdit[] edits) => Writer.SerializeDocument(Sample, edits);

    private static DocumentTranslationEdit Update(string key, Func<DocumentTranslation, DocumentTranslation> change) =>
        new(P(key), change(Current(Sample, key)));

    [Fact]
    public void ReadingGivesMessagesWithTheirDescriptionAndMetadataAndSkipsHeadersAndNonStrings()
    {
        var all = Reader.LoadDocument(Sample);
        Assert.Equal(["greeting", "bye", "plain"], all.Select(t => t.KeyPath.Segments[0]));
        var greeting = all[0];
        Assert.Equal("Shown on the home screen", greeting.Comment);
        Assert.Contains("x-custom", greeting.FormatData![ArbFormat.Placeholders]);
        Assert.Contains("x-owner", greeting.FormatData[ArbFormat.Metadata]);
        Assert.Equal(string.Empty, all[1].Comment);
        Assert.Null(all[1].FormatData);
    }

    [Fact]
    public void AnUnchangedEntryWritesTheSameBytes() =>
        Assert.Same(Sample, Save(Update("greeting", t => t)));

    [Fact]
    public void AChangedValueReplacesOnlyItsToken()
    {
        var saved = Save(Update("bye", t => t with { Value = "See you" }));
        Assert.Equal(Sample.Replace("\"Goodbye\"", "\"See you\""), saved);
    }

    [Fact]
    public void AChangedDescriptionReplacesOnlyItsToken()
    {
        var saved = Save(Update("greeting", t => t with { Comment = "Home screen title" }));
        Assert.Equal(Sample.Replace("Shown on the home screen", "Home screen title"), saved);
    }

    [Fact]
    public void AddingADescriptionPutsItInTheMessagesBlockAndKeepsTheRest()
    {
        var saved = Save(Update("plain", t => t with { Comment = "Footer text" }), Update("bye", t => t with { Comment = "Logout toast" }));
        var plain = Current(saved, "plain");
        Assert.Equal("Footer text", plain.Comment);
        Assert.Equal("Logout toast", Current(saved, "bye").Comment);
        var greeting = Current(saved, "greeting");
        Assert.Equal(Current(Sample, "greeting").FormatData![ArbFormat.Metadata].Replace(" ", "").Replace("\n", ""),
            greeting.FormatData![ArbFormat.Metadata].Replace(" ", "").Replace("\n", ""));
        Assert.True(saved.IndexOf("\"@bye\"", StringComparison.Ordinal) > saved.IndexOf("\"bye\"", StringComparison.Ordinal));
        Assert.True(saved.IndexOf("\"@bye\"", StringComparison.Ordinal) < saved.IndexOf("\"count\"", StringComparison.Ordinal));
        Assert.Contains("\"@@x-generated\": true", saved);
        Assert.Contains("\"count\": 3", saved);
        Assert.Contains("    \"", saved); // The file's four-space indentation is kept.
    }

    [Fact]
    public void RemovingTheOnlyFieldOfABlockRemovesTheBlockButKeepsPlaceholdersElsewhere()
    {
        var withComment = Save(Update("plain", t => t with { Comment = "x" }));
        var removed = Writer.SerializeDocument(withComment, [new(P("plain"), Current(withComment, "plain") with { Comment = string.Empty })]);
        Assert.DoesNotContain("@plain", removed);

        var noDescription = Save(Update("greeting", t => t with { Comment = string.Empty }));
        Assert.DoesNotContain("Shown on the home screen", noDescription);
        Assert.Contains("x-custom", noDescription);
        Assert.Contains("x-owner", noDescription);
    }

    [Fact]
    public void ARenameMovesTheBlockWithItsMessageInPlace()
    {
        var saved = Save(new DocumentTranslationEdit(P("greeting"), Current(Sample, "greeting") with { KeyPath = P("welcome") }));
        var keys = Reader.LoadDocument(saved).Select(t => t.KeyPath.Segments[0]).ToList();
        Assert.Equal(["welcome", "bye", "plain"], keys);
        Assert.DoesNotContain("\"@greeting\"", saved);
        var welcome = Current(saved, "welcome");
        Assert.Equal("Shown on the home screen", welcome.Comment);
        Assert.Contains("x-custom", welcome.FormatData![ArbFormat.Placeholders]);
        Assert.True(saved.IndexOf("\"@welcome\"", StringComparison.Ordinal) < saved.IndexOf("\"bye\"", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeletionRemovesTheMessageAndItsBlockAndKeepsTheHeaders()
    {
        var saved = Save(new DocumentTranslationEdit(P("greeting"), null));
        Assert.DoesNotContain("greeting", saved);
        Assert.DoesNotContain("x-owner", saved);
        Assert.Contains("\"@@locale\": \"en\"", saved);
    }

    [Fact]
    public void ANewMessageIsAddedWithItsDescriptionAndCarriedMetadata()
    {
        var copy = Current(Sample, "greeting") with { KeyPath = P("greeting_copy") };
        var saved = Save(new DocumentTranslationEdit(null, copy), new DocumentTranslationEdit(null, new DocumentTranslation(P("fresh"), "New") { Comment = "Added" }));
        Assert.Equal("Shown on the home screen", Current(saved, "greeting_copy").Comment);
        Assert.Contains("x-owner", Current(saved, "greeting_copy").FormatData![ArbFormat.Metadata]);
        Assert.Equal("Added", Current(saved, "fresh").Comment);
        Assert.Equal("Hello {name}", Current(saved, "greeting").Value);
    }

    [Fact]
    public void EditedPlaceholdersReplaceTheBlocksPlaceholdersAndKeepItsOtherFields()
    {
        var saved = Save(Update("greeting", t => t with
        {
            FormatData = new Dictionary<string, string>(t.FormatData!) { [ArbFormat.Placeholders] = """{"name":{"type":"String","example":"Grace"}}""" },
        }));
        var greeting = Current(saved, "greeting");
        Assert.Contains("Grace", greeting.FormatData![ArbFormat.Placeholders]);
        Assert.DoesNotContain("x-custom", greeting.FormatData[ArbFormat.Placeholders]);
        Assert.Contains("x-owner", greeting.FormatData[ArbFormat.Metadata]);
        Assert.Equal("Shown on the home screen", greeting.Comment);
    }

    [Fact]
    public void ADestinationThatAlreadyExistsIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => Save(new DocumentTranslationEdit(null, new DocumentTranslation(P("bye"), "x"))));
}
