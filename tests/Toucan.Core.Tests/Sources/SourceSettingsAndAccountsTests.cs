using Toucan.Core.Models;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

public sealed class SourceSettingsAndAccountsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-sources-").FullName;
    public void Dispose() => TempFolder.TryDelete(_root);

    [Fact]
    public void LegacyConnectionIdsBecomeOwnerlessSourcesAndAreWrittenAsSources()
    {
        File.WriteAllText(Path.Combine(_root, "toucan.tproj"), """
            { "primaryLanguage": "en", "languages": ["en"], "sourceConnections": ["old-1", "both"],
              "sources": [ { "id": "both", "plugin": "acme.sync" } ] }
            """);
        var settings = ProjectSettings.LoadFrom(_root)!;

        Assert.Equal(["both", "old-1"], settings.Sources.Select(s => s.Id).Order());
        Assert.Equal("acme.sync", settings.Sources.Single(s => s.Id == "both").Plugin);
        Assert.False(settings.Sources.Single(s => s.Id == "old-1").HasOwner);

        settings.Save();
        var json = File.ReadAllText(Path.Combine(_root, "toucan.tproj"));
        Assert.DoesNotContain("sourceConnections", json, StringComparison.Ordinal);
        Assert.Contains("\"sources\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionRecordsRoundTripWithoutCredentials()
    {
        var settings = ProjectSettings.CreateDefault(_root);
        settings.Sources.Add(new SourceConnectionSettings
        {
            Id = "con_1", Adapter = "acme.lokalise.api", BaseUrl = new Uri("https://api.lokalise.com/api2/"),
            RemoteProject = new() { Id = "4821.abc", Name = "Checkout" }, Role = SourceRoles.Primary,
            Languages = new() { ["fr-FR"] = "fr_FR" }, Resources = [new() { Remote = "web/checkout.json", Package = "checkout" }],
            Keys = new() { Separator = "::", CaseSensitive = false }, Policy = new() { ConnectOnOpen = true, DeleteRemote = "never" },
        });
        settings.Save();

        var read = ProjectSettings.LoadFrom(_root)!.Sources.Single();
        Assert.Equal("acme.lokalise.api", read.Adapter);
        Assert.Equal(new Uri("https://api.lokalise.com/api2/"), read.BaseUrl);
        Assert.Equal("Checkout", read.RemoteProject!.Name);
        Assert.Equal("fr_FR", read.Languages["fr-FR"]);
        Assert.Equal("checkout", read.Resources.Single().Package);
        Assert.Equal("::", read.Keys.Separator);
        Assert.True(read.Policy.ConnectOnOpen);
        Assert.Equal("never", read.Policy.DeleteRemote);
        Assert.Equal("con_1", read.Clone().Id);
    }

    [Fact]
    public void AccountsResolveByAdapterAndInstanceAndRememberChoices()
    {
        var store = new SourceAccountStore(Path.Combine(_root, "accounts.json"));
        var api = new Uri("https://api.example.com/v2/");
        Assert.Equal(AccountResolution.None, store.Resolve("acme.x", api, _root, "c1").Resolution);

        var first = store.Add("acme.x", new Uri("https://api.example.com/"), "token", "Work");
        store.Add("acme.x", new Uri("https://other.example.com/"), "token", "Elsewhere");
        var single = store.Resolve("ACME.X", api, _root, "c1");
        Assert.Equal(AccountResolution.Resolved, single.Resolution);
        Assert.Equal(first.Id, single.Account!.Id);

        var second = store.Add("acme.x", new Uri("https://api.example.com/"), "oauth", "Personal");
        Assert.Equal(AccountResolution.Ambiguous, store.Resolve("acme.x", api, _root, "c1").Resolution);
        store.Bind(_root, "c1", second.Id);
        Assert.Equal(second.Id, store.Resolve("acme.x", api, _root, "c1").Account!.Id);
        Assert.Equal(AccountResolution.Ambiguous, store.Resolve("acme.x", api, _root, "c2").Resolution);

        // Another process (here: another instance on the same file) sees the change, and removal drops the choice.
        var other = new SourceAccountStore(store.FilePath);
        Assert.Equal(3, other.Accounts.Count);
        Assert.True(other.Remove(second.Id));
        Assert.Equal(first.Id, store.Resolve("acme.x", api, _root, "c1").Account!.Id);
        Assert.DoesNotContain(_root, File.ReadAllText(store.FilePath), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(store.FilePath));
    }

    [Fact]
    public void KeyNamesRoundTripThroughEscaping()
    {
        var mapper = new SourceKeyMapper(new SourceKeyMapping());
        string[][] paths = [["a", "b"], ["a.b"], ["back\\slash", "x"], ["trail\\"], [""], ["a", "", "b"]];
        foreach (var path in paths)
            Assert.Equal(path, mapper.FromRemote(mapper.ToRemote(path)));
        Assert.Equal("a\\.b", mapper.ToRemote(["a.b"]));
        Assert.NotEqual(mapper.ToRemote(["a", "b"]), mapper.ToRemote(["a.b"]));

        var custom = new SourceKeyMapper(new SourceKeyMapping { Separator = "::" });
        Assert.Equal(["errors", "required"], custom.FromRemote("errors::required"));
        Assert.Equal(["a:b"], custom.FromRemote("a:b"));
        Assert.Throws<ArgumentException>(() => new SourceKeyMapper(new SourceKeyMapping { Separator = "" }));
    }

    [Fact]
    public void CaseInsensitiveServicesReportCollisions()
    {
        IReadOnlyList<string>[] paths = [["Errors", "required"], ["errors", "required"], ["errors", "other"]];
        Assert.Empty(new SourceKeyMapper(new SourceKeyMapping()).Collisions(paths));
        var collision = Assert.Single(new SourceKeyMapper(new SourceKeyMapping { CaseSensitive = false }).Collisions(paths));
        Assert.Equal(2, collision.Count);
    }

    [Fact]
    public void LanguagesAreSuggestedExactlyOrForConfirmation()
    {
        var suggestions = SourceMapping.SuggestLanguages(["en-US", "fr", "zh-Hans", "pt-BR", "ja"],
            [new("en_US"), new("fr_FR"), new("zh_CN"), new("pt_BR"), new("pt_PT")]);
        Assert.Equal(new LanguageSuggestion("en-US", "en_US", true), suggestions[0]);
        Assert.Equal(new LanguageSuggestion("fr", "fr_FR", false), suggestions[1]);
        Assert.Equal(new LanguageSuggestion("zh-Hans", "zh_CN", false), suggestions[2]);
        Assert.Equal(new LanguageSuggestion("pt-BR", "pt_BR", true), suggestions[3]);
        Assert.Equal(new LanguageSuggestion("ja", null, false), suggestions[4]);
    }

    [Fact]
    public void MappingProblemsAreListed()
    {
        var connection = new SourceConnectionSettings
        {
            Id = "c", Role = "both", BaseUrl = new Uri("http://api.example.com/"),
            Languages = new() { ["en"] = "en", ["en-GB"] = "EN", ["de"] = "de" },
            Resources = [new() { Remote = "a" }, new() { Remote = "a" }, new() { Remote = "missing" }],
            RemoteProject = new() { Id = "p1" },
            Keys = new() { Separator = "\\" },
        };
        var remote = new SourceProjectInfo("p2", "P", "en", [new("en"), new("de")], [new("a", "a")], SourceCapabilities.ReadUnits);

        var problems = SourceMapping.Validate(connection, ["en", "en-GB", "fr"], remote);

        Assert.Contains(problems, p => p.Contains("not mirror or primary", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("HTTPS", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("backslash", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Language de is mapped but is not in the project", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Remote language en is mapped from en and en-GB", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("resource a is mapped more than once", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("missing does not exist", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("remote project p1", StringComparison.Ordinal));
        Assert.Equal(["fr"], SourceMapping.Unmapped(connection, ["en", "en-GB", "fr"]).Except(["en-GB"]));
        Assert.Empty(SourceMapping.Validate(new SourceConnectionSettings { Id = "ok", Languages = new() { ["en"] = "en" } }, ["en"], remote with { RemoteProjectId = "p2" }));
    }
}
