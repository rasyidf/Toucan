using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Modules;

// A baseline observer, not a suite that requires known bugs to remain broken.
// Each run retains independently authored inputs/outputs in a fresh directory.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
if (!File.Exists(Path.Combine(root, "Toucan.CrossPlatform.slnx")))
    throw new ArgumentException("Pass the Toucan repository root.");
var run = Path.Combine(root, "build", "resource-compatibility", "m0", "probes", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ"));
Directory.CreateDirectory(run);
using var services = new ServiceCollection().AddLogging().AddToucanCore().AddToucanDefaults().BuildServiceProvider();
var projects = services.GetRequiredService<IProjectService>();
var factory = services.GetRequiredService<ITranslationStrategyFactory>();
var packages = new PackageService(services.GetRequiredService<IFileService>(),
    services.GetServices<ILoadStrategy>(), services.GetServices<ISaveStrategy>());
var results = new List<object>();
var errors = 0;

void Observe(string id, Func<string, object> action)
{
    var folder = Path.Combine(run, id);
    Directory.CreateDirectory(folder);
    try { results.Add(new { id, execution = "completed", observed = action(folder) }); }
    catch (Exception e) { errors++; results.Add(new { id, execution = "error", exception = e.GetType().Name, e.Message }); }
}

void Resource(string folder, string relative, string text)
{
    var file = Path.Combine(folder, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, text);
}

void Babel(string folder, string framework, string resource, string config = "", string version = "1.3")
{
    var document = new XElement("babeledit_project", new XAttribute("version", version),
        new XElement("framework", framework), new XElement("primary_language", "en"),
        new XElement("languages", new XElement("language", new XElement("code", "en"))),
        new XElement("translation_packages", new XElement("translation_package", new XElement("name", "main"),
            new XElement("translation_urls", new XElement("translation_url", new XElement("language", "en"), new XElement("path", resource))))),
        XElement.Parse("<configuration>" + config + "</configuration>"));
    document.Save(Path.Combine(folder, "project.babel"));
}

SaveContext Context(List<TranslationItem> entries) => new()
{
    Languages = entries.Select(i => i.Language).Distinct().ToList(), NsTreeItems = [],
    LanguageDictionary = entries.GroupBy(i => i.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList())
};

Observe("G1-nonjson-explicit-path", folder =>
{
    Resource(folder, "custom/english.yml", "title: Intended\n");
    Resource(folder, "de.yml", "unrelated: Sibling\n");
    Babel(folder, "generic-yaml", "custom/english.yml");
    var loaded = projects.LoadProject(folder);
    return new { entries = loaded.Translations.Select(i => new { i.Language, i.Namespace, i.Value }), loaded.Warnings };
});
Observe("G2-package-yaml-load", folder =>
{
    Resource(folder, "custom/english.yml", "title: Intended\n");
    Babel(folder, "generic-yaml", "custom/english.yml");
    var settings = projects.LoadProject(folder).Settings;
    return new { entries = packages.LoadPackage(settings, "main").Select(i => new { i.Language, i.Namespace, i.Value }) };
});
Observe("G2-package-json-save", folder =>
{
    Resource(folder, "custom/english.json", "{\"title\":\"Before\"}");
    Babel(folder, "generic-json", "custom/english.json", "<format>json</format>");
    var settings = projects.LoadProject(folder).Settings;
    var entries = packages.LoadPackage(settings, "main");
    entries.Single().Value = "After";
    packages.SavePackage(settings, "main", entries);
    return new { intended = File.ReadAllText(Path.Combine(folder, "custom/english.json")), files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(folder, p)) };
});
Observe("G3-literal-nested-collision", folder =>
{
    Resource(folder, "en.json", "{\"a.b\":\"Literal\",\"a\":{\"b\":\"Nested\"}}");
    var entries = factory.GetLoadStrategy(FormatIds.Json)!.Load(folder).ToList();
    var before = entries.Select(i => new { i.Namespace, i.Value }).ToArray();
    factory.GetSaveStrategy(FormatIds.Json)!.Save(folder, Context(entries));
    return new { before, saved = File.ReadAllText(Path.Combine(folder, "en.json")) };
});
Observe("G5-imported-settings", folder =>
{
    Resource(folder, "en.json", "{\"z\":\"Last\",\"a\":\"First\"}");
    Babel(folder, "generic-json", "en.json", "<format>json</format><indent>none</indent><support_arrays>false</support_arrays>");
    var xml = XDocument.Load(Path.Combine(folder, "project.babel"));
    xml.Root!.Add(new XElement("editor_configuration", new XElement("translation_order", "file")));
    xml.Save(Path.Combine(folder, "project.babel"));
    var loaded = projects.LoadProject(folder);
    projects.Save(loaded.Settings, [], loaded.Translations);
    return new { loaded.Settings.TranslationOrder, retained = loaded.Settings.BabelImport!.Configuration, saved = File.ReadAllText(Path.Combine(folder, "en.json")) };
});
Observe("G6-arb-description-edit", folder =>
{
    Resource(folder, "app_en.arb", "{\"@@locale\":\"en\",\"title\":\"Hello\",\"@title\":{\"description\":\"Old description\",\"custom\":\"Keep me\"}}");
    var entries = factory.GetLoadStrategy(FormatIds.Arb)!.Load(folder).ToList();
    var initialComment = entries.Single().Comment;
    entries.Single().Comment = "New description";
    factory.GetSaveStrategy(FormatIds.Arb)!.Save(folder, Context(entries));
    return new { initialComment, saved = File.ReadAllText(Path.Combine(folder, "app_en.arb")) };
});
Observe("G8-json-null", folder =>
{
    Resource(folder, "en.json", "{\"nil\":null,\"title\":\"Hello\"}");
    var entries = factory.GetLoadStrategy(FormatIds.Json)!.Load(folder).ToList();
    factory.GetSaveStrategy(FormatIds.Json)!.Save(folder, Context(entries));
    return new { editableKeys = entries.Select(i => i.Namespace), saved = File.ReadAllText(Path.Combine(folder, "en.json")) };
});
Observe("S1-unknown-project-version", folder =>
{
    Resource(folder, "en.json", "{\"title\":\"Hello\"}");
    Babel(folder, "generic-json", "en.json", "<format>json</format>", "999.0");
    var loaded = projects.LoadProject(folder);
    return new { count = loaded.Translations.Count, loaded.Warnings };
});
Observe("S2-unsupported-formatjs", folder =>
{
    Babel(folder, "react-formatjs-extract", "en.json");
    try { projects.LoadProject(folder); return new { rejected = false, diagnostic = "" }; }
    catch (FormatException e) { return new { rejected = true, diagnostic = e.Message }; }
});
Observe("S3-generic-yaml-locale-root", folder =>
{
    Resource(folder, "en.yml", "en:\n  title: Literal locale-named group\n");
    Babel(folder, "generic-yaml", "en.yml");
    var loaded = projects.LoadProject(folder);
    return new { entries = loaded.Translations.Select(i => new { i.Namespace, i.Value, i.FormatData }) };
});

var output = Path.Combine(run, "observations.json");
File.WriteAllText(output, JsonSerializer.Serialize(new { schemaVersion = 1, generatedUtc = DateTime.UtcNow, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);
Environment.ExitCode = errors == 0 ? 0 : 1;
