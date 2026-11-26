using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Formats;

/// <summary>
/// Document saving for Java <c>.properties</c> (V1-11): only edited entries change; comments, separators, continuations, escapes,
/// line endings and ISO-8859-1 bytes stay as they were.
/// </summary>
public sealed class PropertiesDocumentTests : IDisposable
{
    private const string Sample = "# Greetings\r\n! shown on the home page\r\ngreeting = Hello\\, {0}\r\n\r\nlong: first part \\\r\n    second part\r\nmenu.file=File\r\nmenu.file=Fichier ignored\r\nmenu.file=File menu\r\n";

    private static IDocumentSaveStrategy Writer => (IDocumentSaveStrategy)FormatTestHost.Factory.GetSaveStrategy(FormatIds.JavaProperties)!;
    private static IDocumentLoadStrategy Reader => (IDocumentLoadStrategy)FormatTestHost.Factory.GetLoadStrategy(FormatIds.JavaProperties)!;
    private static TranslationPath P(string key) => new([key]);
    private static DocumentTranslation Current(string content, string key) => Reader.LoadDocument(content).Single(t => t.KeyPath.Equals(P(key)));
    private static string Save(params DocumentTranslationEdit[] edits) => Writer.SerializeDocument(Sample, edits);

    private readonly string _root = Directory.CreateTempSubdirectory("toucan-properties-document-").FullName;
    private readonly ServiceProvider _services;

    public PropertiesDocumentTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults();
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void ReadingTakesEachKeysLastValueAndJoinsContinuations()
    {
        var all = Reader.LoadDocument(Sample);
        Assert.Equal(["greeting", "long", "menu.file"], all.Select(t => t.KeyPath.Segments[0]));
        Assert.Equal("Hello, {0}", all[0].Value);
        Assert.Equal("first part second part", all[1].Value);
        Assert.Equal("File menu", all[2].Value);
    }

    [Fact]
    public void AnUnchangedEntryWritesTheSameText() =>
        Assert.Equal(Sample, Save(new DocumentTranslationEdit(P("long"), Current(Sample, "long"))));

    [Fact]
    public void AChangedValueRewritesOnlyItsLineAndKeepsLineEndings()
    {
        var saved = Save(new DocumentTranslationEdit(P("long"), Current(Sample, "long") with { Value = "short" }));
        Assert.Equal(Sample.Replace("long: first part \\\r\n    second part\r\n", "long=short\r\n"), saved);
    }

    [Fact]
    public void TheLastOccurrenceOfARepeatedKeyIsTheOneEdited()
    {
        var saved = Save(new DocumentTranslationEdit(P("menu.file"), Current(Sample, "menu.file") with { Value = "Archive" }));
        Assert.Contains("menu.file=File\r\nmenu.file=Fichier ignored\r\nmenu.file=Archive\r\n", saved);
        Assert.Equal("Archive", Current(saved, "menu.file").Value);
    }

    [Fact]
    public void RenamesStayInPlaceDeletionsDropTheirLineAndNewKeysGoLast()
    {
        var saved = Save(new DocumentTranslationEdit(P("greeting"), Current(Sample, "greeting") with { KeyPath = P("welcome") }),
            new DocumentTranslationEdit(P("long"), null), new DocumentTranslationEdit(null, new DocumentTranslation(P("new key"), " padded")));
        Assert.StartsWith("# Greetings\r\n! shown on the home page\r\nwelcome=Hello, {0}\r\n\r\nmenu.file", saved);
        Assert.EndsWith("new\\ key=\\ padded\r\n", saved);
        Assert.Equal(" padded", Current(saved, "new key").Value);
    }

    [Fact]
    public void ExistingUnicodeEscapesAreKeptAsTheStyleForNewText()
    {
        var original = "title=Caf\\u00e9\n";
        var saved = Writer.SerializeDocument(original, [new(P("title"), new DocumentTranslation(P("title"), "Crème"))]);
        Assert.Equal("title=Cr\\u00E8me\n", saved);
    }

    [Fact]
    public void ADestinationThatAlreadyExistsIsRefused() =>
        Assert.Throws<InvalidOperationException>(() => Save(new DocumentTranslationEdit(null, new DocumentTranslation(P("greeting"), "x"))));

    // ───────────── projects ─────────────

    private void Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private byte[] ReadBytes(string relative) => File.ReadAllBytes(Path.Combine(_root, relative));

    private (ProjectLoadResult Loaded, WorkspaceService Workspace) Open()
    {
        var loaded = _services.GetRequiredService<IProjectService>().LoadProject(_root);
        var workspace = _services.GetRequiredService<WorkspaceService>();
        if (loaded.Source is not null) workspace.Open(loaded.Settings, loaded.Source);
        return (loaded, workspace);
    }

    private const string Resources = "src/main/resources/";

    private void CreateBundle()
    {
        Write("pom.xml", "<project/>"u8.ToArray());
        Write(Resources + "messages.properties", "# Base bundle\ngreeting=Hello\nfarewell=Bye\n"u8.ToArray());
        // ISO-8859-1, as Java expects by default: é is the single byte 0xE9.
        Write(Resources + "messages_fr.properties", Encoding.Latin1.GetBytes("# Français\ngreeting=Bonjour\nfarewell=Au revoir, l'été\n"));
        Write(Resources + "messages_fr_CA.properties", "greeting=Allo\n"u8.ToArray());
    }

    [Fact]
    public void ABundleOpensThroughDocumentSavingWithLocalesFromTheFileNames()
    {
        CreateBundle();
        var (loaded, _) = Open();
        Assert.Equal("java-resourcebundle", loaded.Settings.Framework);
        Assert.NotNull(loaded.Source);
        Assert.Equal(["default", "fr", "fr-CA"], loaded.Source!.Documents.Select(d => d.Language).Order(StringComparer.Ordinal));
        Assert.Equal("Au revoir, l'été", loaded.Translations.Single(t => t.Language == "fr" && t.Namespace == "farewell").Value);
        Assert.Equal(2, loaded.Source.Units.Select(u => u.EntryId).Distinct().Count());
    }

    [Fact]
    public async Task SavingAnIso88591FileChangesOnlyTheEditedLineAndStaysIso88591()
    {
        CreateBundle();
        var (_, workspace) = Open();
        var snapshot = (await workspace.SnapshotEntriesAsync())!;
        var greeting = snapshot.Units.Single(u => u.Key == "greeting" && u.Language == "fr");
        Assert.True((await workspace.ApplyEntriesAsync("editor", new(snapshot.SessionId, snapshot.Revision,
            [new SetEntryValue(greeting.EntryId, "fr", "Bonjour à tous ✓")]))).Applied);
        await _services.GetRequiredService<WorkspaceDocumentSaveCoordinator>().SaveAsync(workspace.Source!);

        var bytes = ReadBytes(Resources + "messages_fr.properties");
        Assert.Equal(Encoding.Latin1.GetBytes("# Français\ngreeting=Bonjour \u00e0 tous \\u2713\nfarewell=Au revoir, l'été\n"), bytes);
        Assert.Equal("# Base bundle\ngreeting=Hello\nfarewell=Bye\n"u8.ToArray(), ReadBytes(Resources + "messages.properties"));

        var (reopened, _) = Open();
        Assert.Equal("Bonjour à tous ✓", reopened.Translations.Single(t => t.Language == "fr" && t.Namespace == "greeting").Value);
    }

    [Fact]
    public void AnOlderProjectWhoseLanguagesDoNotMatchTheBundleKeepsTheFullRewritePath()
    {
        CreateBundle();
        // Saved by the full-rewrite path, which named the base bundle "messages" and fr_CA "CA".
        new ProjectSettings
        {
            ProjectPath = _root, SaveFormat = FormatIds.JavaProperties, PrimaryLanguage = "messages", Languages = ["messages", "fr", "CA"],
            TranslationStates = [new() { Language = "CA", Key = "greeting", Approved = true }],
        }.Save();

        var (loaded, _) = Open();
        Assert.Null(loaded.Source);
        Assert.True(string.IsNullOrEmpty(loaded.Settings.Framework));
        Assert.True(loaded.Translations.Single(t => t.Language == "CA" && t.Namespace == "greeting").IsApproved);
    }

    [Fact]
    public void AnOlderProjectWhoseLanguagesMatchMovesToDocumentSavingAndKeepsApprovals()
    {
        Write("pom.xml", "<project/>"u8.ToArray());
        Write(Resources + "messages_en.properties", "greeting=Hello\n"u8.ToArray());
        Write(Resources + "messages_fr.properties", "greeting=Bonjour\n"u8.ToArray());
        new ProjectSettings
        {
            ProjectPath = _root, SaveFormat = FormatIds.JavaProperties, PrimaryLanguage = "en", Languages = ["en", "fr"],
            TranslationStates = [new() { Language = "fr", Key = "greeting", Approved = true, Comment = "Checked by Lea" }],
        }.Save();

        var (loaded, _) = Open();
        Assert.Equal("java-resourcebundle", loaded.Settings.Framework);
        var fr = loaded.Translations.Single(t => t.Language == "fr" && t.Namespace == "greeting");
        Assert.True(fr.IsApproved);
        Assert.Equal("Checked by Lea", fr.Comment);
    }
}
