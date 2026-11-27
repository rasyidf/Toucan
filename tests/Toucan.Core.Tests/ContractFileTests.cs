using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Toucan.Core.Models;
using Toucan.Core.Sources;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>
/// Frozen file contracts (V1-21 to V1-23): the app manifest's schema is generated from <see cref="ProjectSettings"/>, and each
/// file refuses or sets aside what a newer build wrote instead of overwriting it. See docs/compatibility.md.
/// </summary>
public sealed class ContractFileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-contracts-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private static string RepoFile(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Toucan.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return Path.Combine([root!, .. parts]);
    }

    [Fact]
    public void TheManifestSchemaIsUpToDate()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Default)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        var schema = options.GetJsonSchemaAsNode(typeof(ProjectSettings), new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://toucan.rasyid.dev/toucan.tproj.schema.json",
            ["title"] = "Toucan project (toucan.tproj)",
            ["description"] = $"Generated from ProjectSettings by ContractFileTests; manifest version {ProjectSettings.CurrentManifestVersion}. Unknown fields are kept by older builds. Do not edit by hand.",
        };
        foreach (var (key, value) in schema.AsObject().ToList()) root[key] = value?.DeepClone();
        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n";
        var path = RepoFile("docs", "toucan.tproj.schema.json");
        if (Environment.GetEnvironmentVariable("TOUCAN_UPDATE_DOCS") == "1") File.WriteAllText(path, text);
        Assert.True(File.Exists(path), "docs/toucan.tproj.schema.json is missing. Run the tests with TOUCAN_UPDATE_DOCS=1 to generate it.");
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void SavedManifestsCarryTheirVersionAndOlderOnesReadAsVersionOne()
    {
        new ProjectSettings { ProjectPath = _root, Languages = ["en"] }.Save();
        Assert.Contains("\"manifestVersion\": 1", File.ReadAllText(Path.Combine(_root, "toucan.tproj")));
        File.WriteAllText(Path.Combine(_root, "toucan.tproj"), """{"primaryLanguage":"en","languages":["en"],"saveFormat":"json"}""");
        Assert.Equal(1, ProjectSettings.LoadFrom(_root)!.ManifestVersion);
    }

    [Fact]
    public void ADamagedManifestIsReportedAndNeverReplaced()
    {
        var path = Path.Combine(_root, "toucan.tproj");
        File.WriteAllText(path, """{"primaryLanguage":"en", "translationStates": [""");
        var error = Assert.Throws<FormatException>(() => ProjectSettings.LoadFrom(_root));
        Assert.Contains("will not overwrite", error.Message);
        Assert.Equal("""{"primaryLanguage":"en", "translationStates": [""", File.ReadAllText(path));
    }

    [Fact]
    public void AManifestFromANewerToucanIsRefusedAndItsUnknownFieldsSurviveAnOlderSave()
    {
        var path = Path.Combine(_root, "toucan.tproj");
        File.WriteAllText(path, """{"manifestVersion":2,"primaryLanguage":"en"}""");
        Assert.Contains("newer Toucan", Assert.Throws<FormatException>(() => ProjectSettings.LoadFrom(_root)).Message);

        // A field added in a later 1.x release does not bump the version, and an older build keeps it when saving.
        File.WriteAllText(path, """{"manifestVersion":1,"primaryLanguage":"en","languages":["en"],"addedLater":{"keep":true}}""");
        ProjectSettings.LoadFrom(_root)!.Save();
        Assert.Contains("\"addedLater\"", File.ReadAllText(path));
    }

    [Fact]
    public void AccountsFromANewerToucanAreNeverReplacedAndADamagedFileIsSetAside()
    {
        var file = Path.Combine(_root, "accounts.json");
        File.WriteAllText(file, """{"version":2,"accounts":[{"id":"acc_future"}]}""");
        var store = new SourceAccountStore(file);
        Assert.Empty(store.Accounts);
        Assert.Throws<InvalidOperationException>(() => store.Add("x.adapter", new Uri("https://example.com"), "token", "Example"));
        Assert.Contains("acc_future", File.ReadAllText(file));

        File.WriteAllText(file, "{ not json");
        var fresh = new SourceAccountStore(file);
        fresh.Add("x.adapter", new Uri("https://example.com"), "token", "Example");
        Assert.Single(fresh.Accounts);
        Assert.Single(Directory.GetFiles(_root, "accounts.json.corrupt-*"));
    }

    [Fact]
    public void SyncStateFromANewerToucanIsSetAsideNotOverwritten()
    {
        var store = new Toucan.Core.Sources.SyncStateStore(_root, "c1");
        store.SaveBaseline(new SyncBaseline());
        var path = store.BaselinePath;
        File.WriteAllText(path, File.ReadAllText(path).Replace($"\"Schema\": {SyncBaseline.CurrentSchema}", "\"Schema\": 99"));
        Assert.Null(store.LoadBaseline());
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "baseline.json.corrupt-*"));
    }
}
