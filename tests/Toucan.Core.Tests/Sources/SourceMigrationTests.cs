using Toucan.Core.Models;
using Toucan.Core.Sources;
using Xunit;

namespace Toucan.Core.Tests.Sources;

/// <summary>Files written by earlier preview builds still load, and files from the future are never overwritten.</summary>
public sealed class SourceMigrationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-migrate-").FullName;
    public void Dispose() => TempFolder.TryDelete(_root);

    private static readonly Guid Entry = Guid.Parse("00000000-0000-0000-0000-0000000000aa");

    [Fact]
    public void ABaselineWithFieldsThisBuildDoesNotKnowStillLoads()
    {
        var store = new SyncStateStore(_root, "c1");
        Directory.CreateDirectory(store.Folder);
        File.WriteAllText(store.BaselinePath, $$"""
            { "Schema": 1, "Futurefield": [1, 2],
              "Units": [ { "EntryId": "{{Entry}}", "Language": "fr", "Value": "x", "ValueHash": "h", "Remote": { "ResourceId": "r", "Name": "n" }, "Surprise": true } ] }
            """);

        var baseline = store.LoadBaseline()!;

        Assert.Equal((Entry, "x", "n"), (baseline.Units[0].EntryId, baseline.Units[0].Value, baseline.Units[0].Remote.Name));
        Assert.Null(baseline.Units[0].Status);
        Assert.Null(baseline.DeltaCursor);
        Assert.Empty(Directory.GetFiles(store.Folder, "*.corrupt-*"));
    }

    [Fact]
    public void AJournalMissingLaterAdditionsGetsDefaults()
    {
        // An early preview had neither Members, RemoteLanguage nor Values.
        var store = new SyncStateStore(_root, "c1");
        Directory.CreateDirectory(store.Folder);
        File.WriteAllText(store.JournalPath, $$"""
            { "Schema": 1, "Entries": [ { "OperationId": "{{Guid.NewGuid()}}", "Kind": "PutValue", "EntryId": "{{Entry}}", "Language": "fr",
                                          "Target": { "ResourceId": "r", "Name": "n" }, "State": "InFlight", "Attempts": 1 } ] }
            """);

        var entry = Assert.Single(store.LoadJournal().Entries);

        Assert.Equal((JournalKind.PutValue, JournalState.InFlight), (entry.Kind, entry.State));
        Assert.Empty(entry.Members);
        Assert.Equal(string.Empty, entry.RemoteLanguage);
        Assert.Null(entry.Values);
    }

    [Fact]
    public void AJournalWithAStateFromANewerBuildIsSetAsideNotDropped()
    {
        var store = new SyncStateStore(_root, "c1");
        Directory.CreateDirectory(store.Folder);
        File.WriteAllText(store.JournalPath, """{ "Schema": 1, "Entries": [ { "Kind": "PutValue", "State": "Rolling" } ] }""");

        Assert.Empty(store.LoadJournal().Entries);

        Assert.Single(Directory.GetFiles(store.Folder, "journal.json.corrupt-*"));
    }

    [Fact]
    public void AnAccountsFileFromTheFirstPreviewHasNoBindings()
    {
        var file = Path.Combine(_root, "accounts.json");
        File.WriteAllText(file, """
            { "version": 1, "accounts": [ { "id": "acc_1", "adapter": "toucan.sources.rest", "baseUrl": "https://s.example/", "authMethod": "token", "displayName": "me" } ] }
            """);

        var store = new SourceAccountStore(file);

        var account = Assert.Single(store.Accounts);
        Assert.Equal("me", account.DisplayName);
        Assert.Empty(store.UsageOf("acc_1"));
        store.Bind("/p", "c1", "acc_1");
        Assert.Single(store.UsageOf("acc_1"));
    }

    [Fact]
    public void ProjectsFromBeforeSourcesLoadWithNoConnections()
    {
        File.WriteAllText(Path.Combine(_root, "toucan.tproj"), """{ "primaryLanguage": "en", "languages": ["en", "fr"] }""");
        var settings = ProjectSettings.LoadFrom(_root)!;
        Assert.Empty(settings.Sources);

        settings.Save();
        Assert.Empty(ProjectSettings.LoadFrom(_root)!.Sources);
    }

    [Fact]
    public void ConnectionsKeepTheirMappingPolicyAndSettingsAcrossSaveAndReload()
    {
        var settings = ProjectSettings.CreateDefault(_root);
        settings.Sources.Add(new SourceConnectionSettings
        {
            Id = "c1", Name = "Service", Adapter = "toucan.sources.rest", BaseUrl = new Uri("https://s.example/"), Role = SourceRoles.Primary,
            RemoteProject = new() { Id = "demo", Name = "Demo" }, Languages = { ["fr"] = "fr-FR" }, Resources = [new() { Remote = "app", Package = "web" }],
            Keys = { Separator = "/", CaseSensitive = false }, Policy = { ConnectOnOpen = true, PushComments = true, DeleteRemote = "never" },
        });
        settings.Save();

        var back = Assert.Single(ProjectSettings.LoadFrom(_root)!.Sources);

        Assert.Equal(("demo", "fr-FR", "web", "/", false), (back.RemoteProject!.Id, back.Languages["fr"], back.Resources[0].Package, back.Keys.Separator, back.Keys.CaseSensitive));
        Assert.Equal((true, true, "never", SourceRoles.Primary), (back.Policy.ConnectOnOpen, back.Policy.PushComments, back.Policy.DeleteRemote, back.Role));
    }

    [Fact]
    public void ProjectJsonSchemaDescribesTheSourcesBlock()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Toucan.slnx"))) dir = dir.Parent;
        var schema = Directory.GetFiles(dir!.FullName, "toucan.project.schema.json", SearchOption.AllDirectories).First(f => !f.Contains("/build/", StringComparison.Ordinal));
        var text = File.ReadAllText(schema);
        Assert.Contains("\"sources\"", text, StringComparison.Ordinal);
        Assert.Contains("deleteRemote", text, StringComparison.Ordinal);
    }
}
