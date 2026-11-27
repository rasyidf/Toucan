using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Toucan.Core.Contracts;
using Toucan.Core.Services;
using Toucan.Modules;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>
/// V1-26 and V1-27: projects and settings written by each released preview open in this build with the values, comments and
/// approvals that release itself saw. The fixtures in <c>tests/fixtures/releases/&lt;version&gt;/</c> were saved by that release's own
/// code (open, approve one translation, comment on another, edit a third, save) and <c>expected/</c> is what it read back.
/// </summary>
public sealed class ReleaseMigrationTests : IDisposable
{
    private readonly List<string> _copies = [];

    public void Dispose()
    {
        foreach (var copy in _copies)
            try { Directory.Delete(copy, recursive: true); }
            catch (IOException) { }
    }

    private static string Fixtures
    {
        get
        {
            var root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "Toucan.slnx"))) root = Path.GetDirectoryName(root);
            return Path.Combine(root!, "tests", "fixtures", "releases");
        }
    }

    public static TheoryData<string, string> Projects
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var release in Directory.GetDirectories(Fixtures).Select(Path.GetFileName).Order(StringComparer.Ordinal))
                foreach (var project in Directory.GetDirectories(Path.Combine(Fixtures, release!, "projects")).Select(Path.GetFileName).Order(StringComparer.Ordinal))
                    // 0.23 duplicated YAML and .properties files; these open merged (MIG-01), see the tests below.
                    if (!(release == "0.23.0" && project is "yaml" or "properties")) data.Add(release!, project!);
            return data;
        }
    }

    private sealed record Entry(string Language, string Key, string Value, string Comment, bool Approved);
    private sealed record Expected(string Release, List<Entry> Entries);

    private string Copy(string release, string project)
    {
        var source = Path.Combine(Fixtures, release, "projects", project);
        var target = Directory.CreateTempSubdirectory($"toucan-{release}-{project}-").FullName;
        _copies.Add(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return target;
    }

    /// <summary>
    /// Entries the release saw that are real translations: 0.23 and 0.24 also listed their own sidecars as languages
    /// (<c>.toucan-metadata</c>, <c>fr.json.comments</c>) and a blank key.
    /// </summary>
    private static bool Real(Entry e) => !e.Language.StartsWith('.') && !e.Language.EndsWith(".comments", StringComparison.Ordinal) && e.Key.Length > 0;

    /// <summary>Where 1.0 deliberately reads more than the release did. Each case names why.</summary>
    private static Entry Exceptions(string release, string project, Entry e) => (release, project, e.Language, e.Key) switch
    {
        // 0.23 did not show an ARB message's @key.description as its comment; 1.0 does (V1-10).
        ("0.23.0", "arb", "en", "greeting") => e with { Comment = "Home title" },
        _ => e,
    };

    [Theory]
    [MemberData(nameof(Projects))]
    public async Task AProjectFromAReleasedPreviewOpensWithWhatThatReleaseSaw(string release, string project)
    {
        var expected = JsonSerializer.Deserialize<Expected>(File.ReadAllText(Path.Combine(Fixtures, release, "expected", project + ".json")))!;
        var folder = Copy(release, project);
        var before = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

        using var provider = Provider();
        var lifecycle = provider.GetRequiredService<IProjectLifecycleService>();
        var opened = await lifecycle.OpenProjectAsync(folder);
        Assert.True(opened.Status == ProjectOpenStatus.Success, $"{opened.Status}: {opened.ErrorMessage}");

        static string Line(Entry e) => $"{e.Language} | {e.Key} | {e.Value} | {e.Comment} | {(e.Approved ? "approved" : "-")}";
        var actual = provider.GetRequiredService<ITranslationManagementService>().Translations
            .Select(t => Line(new Entry(t.Language, t.Namespace, t.Value ?? "", t.Comment ?? "", t.IsApproved))).Order(StringComparer.Ordinal).ToList();
        var wanted = expected.Entries.Where(Real).Select(e => Line(Exceptions(release, project, e))).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(wanted, actual);
        // Opening reads only: no file of the old project changes until it is saved.
        if (Environment.GetEnvironmentVariable("TOUCAN_DEBUG_DUMP") is { Length: > 0 } dump)
            foreach (var (file, bytes) in before.Where(p => !p.Value.AsSpan().SequenceEqual(File.ReadAllBytes(p.Key))))
            { File.WriteAllBytes(Path.Combine(dump, "before-" + Path.GetFileName(file)), bytes); File.Copy(file, Path.Combine(dump, "after-" + Path.GetFileName(file)), true); }
        foreach (var (file, bytes) in before) Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(file)), $"Opening changed {Path.GetRelativePath(folder, file)}.");
        // Nothing was edited, so closing must not ask to save (MIG-02).
        AssertUnchanged(provider, lifecycle);
        await lifecycle.CloseProjectAsync();

    }



    private static void AssertUnchanged(ServiceProvider provider, IProjectLifecycleService lifecycle)
    {
        var store = provider.GetRequiredService<ITranslationManagementService>();
        Assert.False(store.IsDirty, "Opened with unsaved changes: " + string.Join(", ", store.GetDirtyItems().Select(t => $"{t.Language}:{t.Namespace}")));
        Assert.False(provider.GetRequiredService<WorkspaceService>().IsDirty, "The workspace opened with unsaved changes.");
        Assert.False(lifecycle.CurrentProject!.IsDirty, "The project settings opened with unsaved changes.");
    }

    /// <summary>The services the desktop app wires for opening and saving; the lifecycle also reads older comment and audit sidecars.</summary>
    private ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddToucanCore(); services.AddToucanDefaults();
        services.AddSingleton(Substitute.For<IFileWatcherService>());
        services.AddSingleton(Substitute.For<IAutoSaveService>());
        services.AddSingleton(Substitute.For<IRecentProjectService>());
        // As the desktop app registers them (App.axaml.cs); drafts go to a temporary folder, not the user's.
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<IDiffMergeEngine, DiffMergeEngine>();
        services.AddSingleton<LanguageManagementService>();
        var drafts = Directory.CreateTempSubdirectory("toucan-drafts-").FullName;
        _copies.Add(drafts);
        services.AddSingleton<IRecoveryDraftService>(new RecoveryDraftService(drafts, null));
        services.AddSingleton<IProjectLifecycleService>(sp =>
        {
            // The app's own wiring: every service the constructor takes, from the container where registered.
            var ctor = typeof(ProjectLifecycleService).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            return (ProjectLifecycleService)ctor.Invoke([.. ctor.GetParameters().Select(p => sp.GetService(p.ParameterType) ?? (p.HasDefaultValue ? p.DefaultValue : null))]);
        });
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("yaml", "config/locales/en.yml", "en.yaml", "en.farewell", "Bye", "farewell: Bye")]
    [InlineData("properties", "src/main/resources/messages_en.properties", "en.properties", "farewell", "Goodbye", "farewell=Goodbye")]
    public async Task A023ProjectWithCopiedFilesOpensMergedAndTheNextSaveSetsTheCopiesAside(string project, string original, string copy,
        string key, string value, string savedLine)
    {
        var folder = Copy("0.23.0", project);
        using var provider = Provider();
        var lifecycle = provider.GetRequiredService<IProjectLifecycleService>();
        var opened = await lifecycle.OpenProjectAsync(folder);
        Assert.True(opened.Status == ProjectOpenStatus.Success, $"{opened.Status}: {opened.ErrorMessage}");
        Assert.Contains(opened.Warnings ?? [], w => w.Contains("Toucan 0.23 saved copies", StringComparison.Ordinal) && w.Contains(copy, StringComparison.Ordinal));
        var store = provider.GetRequiredService<ITranslationManagementService>();
        Assert.Equal(4, store.Translations.Count);
        // 0.23 named Rails keys with their locale root (en.farewell); this build reads them without it.
        Assert.Equal(value, store.Translations.Single(t => t.Language == "en" && (t.Namespace == key || key.EndsWith("." + t.Namespace, StringComparison.Ordinal))).Value);
        Assert.Equal("Checked by Lea", store.Translations.Single(t => t.Language == "fr" && t.Namespace.EndsWith("farewell", StringComparison.Ordinal)).Comment);
        // Opening writes nothing; the copies stay until a save has replaced them.
        Assert.True(File.Exists(Path.Combine(folder, copy)));
        // The merged values are not in the original files yet, so the project opens with something to save.
        Assert.True(store.IsDirty);

        Assert.Equal(ProjectSaveStatus.Success, (await lifecycle.SaveProjectAsync()).Status);
        await lifecycle.CloseProjectAsync();
        Assert.Contains(savedLine, File.ReadAllText(Path.Combine(folder, original)));
        Assert.False(File.Exists(Path.Combine(folder, copy)));
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(folder, ".toucan", "backup", "0.23-copies"), copy, SearchOption.AllDirectories));
        Assert.Contains(value == "Goodbye" ? "Goodbye" : "Bye", File.ReadAllText(backup));

        using var again = Provider();
        var reopened = again.GetRequiredService<IProjectLifecycleService>();
        var second = await reopened.OpenProjectAsync(folder);
        Assert.DoesNotContain(second.Warnings ?? [], w => w.Contains("Toucan 0.23 saved copies", StringComparison.Ordinal));
        var entries = again.GetRequiredService<ITranslationManagementService>().Translations;
        Assert.Equal(4, entries.Count);
        Assert.Equal(value, entries.Single(t => t.Language == "en" && t.Namespace.EndsWith("farewell", StringComparison.Ordinal)).Value);
        Assert.Equal("Checked by Lea", entries.Single(t => t.Language == "fr" && t.Namespace.EndsWith("farewell", StringComparison.Ordinal)).Comment);
        await reopened.CloseProjectAsync();
    }

    [Fact]
    public async Task AnArbCommentFromA023SidecarIsWrittenIntoTheFileOnTheFirstSave()
    {
        var folder = Copy("0.23.0", "arb");
        using var provider = Provider();
        var lifecycle = provider.GetRequiredService<IProjectLifecycleService>();
        Assert.Equal(ProjectOpenStatus.Success, (await lifecycle.OpenProjectAsync(folder)).Status);
        Assert.Equal(ProjectSaveStatus.Success, (await lifecycle.SaveProjectAsync()).Status);
        await lifecycle.CloseProjectAsync();

        var french = File.ReadAllText(Path.Combine(folder, "lib", "l10n", "app_fr.arb"));
        Assert.Contains("\"description\": \"Checked by Lea\"", french);
        using var reopened = Provider();
        var again = reopened.GetRequiredService<IProjectLifecycleService>();
        await again.OpenProjectAsync(folder);
        Assert.Equal("Checked by Lea", reopened.GetRequiredService<ITranslationManagementService>().Translations.Single(t => t.Language == "fr" && t.Namespace == "farewell").Comment);
        await again.CloseProjectAsync();
    }

    [Theory]
    [InlineData("0.23.0")]
    [InlineData("0.24.0")]
    [InlineData("0.25.0")]
    [InlineData("0.26.0")]
    public void SettingsFromAReleasedPreviewKeepTheirValues(string release)
    {
        var text = File.ReadAllText(Path.Combine(Fixtures, release, "user", "settings.json"));
        var options = JsonSerializer.Deserialize<AppOptions>(text)!;
        Assert.Equal(250, options.PageSize);
        Assert.Equal(7, options.RecentProjectsLimit);
        // Settings this build does not know are kept when it writes the file again.
        var reserialized = JsonSerializer.Serialize(options);
        using var original = JsonDocument.Parse(text);
        using var written = JsonDocument.Parse(reserialized);
        foreach (var property in original.RootElement.EnumerateObject())
            Assert.True(written.RootElement.TryGetProperty(property.Name, out _), $"{property.Name} was dropped.");
    }
}
