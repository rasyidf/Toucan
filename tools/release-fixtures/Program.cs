// Writes projects and settings the way one Toucan release saves them, for migration fixtures (V1-26).
// Compiled against each release's own Toucan.Core; run with HOME pointed at a scratch folder.
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Modules;
using NSubstitute;
using Toucan.Core.Services;

var output = Path.GetFullPath(args[0]);
var version = args[1];
Directory.CreateDirectory(output);

void Write(string root, string relative, string text)
{
    var path = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
}

var scenarios = new (string Name, string Format, (string Path, string Text)[] Files, string? Framework)[]
{
    ("json", FormatIds.Json, [("en.json", "{\n  \"greeting\": \"Hello\",\n  \"farewell\": \"Bye\"\n}\n"), ("fr.json", "{\n  \"greeting\": \"Bonjour\",\n  \"farewell\": \"Salut\"\n}\n")], null),
    ("i18next", FormatIds.Namespaced, [("locales/en/common.json", "{\n  \"greeting\": \"Hello\",\n  \"farewell\": \"Bye\"\n}\n"), ("locales/fr/common.json", "{\n  \"greeting\": \"Bonjour\",\n  \"farewell\": \"Salut\"\n}\n")], "i18next"),
    ("arb", FormatIds.Arb, [("pubspec.yaml", "name: demo\n"), ("lib/l10n/app_en.arb", "{\n  \"@@locale\": \"en\",\n  \"greeting\": \"Hello {name}\",\n  \"@greeting\": {\"description\": \"Home title\", \"placeholders\": {\"name\": {\"type\": \"String\"}}},\n  \"farewell\": \"Bye\"\n}\n"), ("lib/l10n/app_fr.arb", "{\n  \"@@locale\": \"fr\",\n  \"greeting\": \"Bonjour {name}\",\n  \"farewell\": \"Salut\"\n}\n")], null),
    ("properties", FormatIds.JavaProperties, [("pom.xml", "<project/>\n"), ("src/main/resources/messages_en.properties", "# English\ngreeting=Hello\nfarewell=Bye\n"), ("src/main/resources/messages_fr.properties", "# French\ngreeting=Bonjour\nfarewell=Salut\n")], null),
    ("yaml", FormatIds.Yaml, [("config/locales/en.yml", "en:\n  greeting: Hello\n  farewell: Bye\n"), ("config/locales/fr.yml", "fr:\n  greeting: Bonjour\n  farewell: Salut\n")], null),
    ("po", FormatIds.Po, [("en.po", "msgid \"greeting\"\nmsgstr \"Hello\"\n\nmsgid \"farewell\"\nmsgstr \"Bye\"\n"), ("fr.po", "msgid \"greeting\"\nmsgstr \"Bonjour\"\n\nmsgid \"farewell\"\nmsgstr \"Salut\"\n")], null),
};

foreach (var scenario in scenarios)
{
    var root = Path.Combine(output, "projects", scenario.Name);
    if (Directory.Exists(root)) Directory.Delete(root, true);
    foreach (var (path, text) in scenario.Files) Write(root, path, text);
    if (scenario.Framework is { } framework)
        new ProjectSettings { ProjectPath = root, Framework = framework, SaveFormat = scenario.Format, PrimaryLanguage = "en", Languages = ["en", "fr"] }.Save();

    await using var provider = BuildProvider();
    var lifecycle = Lifecycle(provider);
    var store = provider.GetRequiredService<ITranslationManagementService>();
    var opened = await lifecycle.OpenProjectAsync(root);

    TranslationItem Item(string language, string key) => store.Translations.Single(t => t.Language == language && t.Namespace.EndsWith(key, StringComparison.Ordinal));
    var approved = Item("fr", "greeting");
    approved.IsApproved = true;
    approved.ApprovedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    store.NotifyValueChanged(approved, approved.Value);
    var commented = Item("fr", "farewell");
    commented.Comment = "Checked by Lea";
    store.NotifyCommentChanged(commented, commented.Comment);
    var edited = Item("en", "farewell");
    edited.Value = "Goodbye";
    store.NotifyValueChanged(edited, edited.Value);

    var saved = await lifecycle.SaveProjectAsync();
    Console.WriteLine($"{version} {scenario.Name}: framework={lifecycle.CurrentProject?.Framework} saved={JsonSerializer.Serialize(saved)}");
    await lifecycle.CloseProjectAsync();

    // What this release itself sees when it opens the saved project again: the migration tests expect the same.
    await using var again = BuildProvider();
    var reopen = Lifecycle(again);
    await reopen.OpenProjectAsync(root);
    var seen = again.GetRequiredService<ITranslationManagementService>().Translations
        .Select(t => new { t.Language, Key = t.Namespace, t.Value, t.Comment, Approved = t.IsApproved })
        .OrderBy(t => t.Language, StringComparer.Ordinal).ThenBy(t => t.Key, StringComparer.Ordinal).ToList();
    await reopen.CloseProjectAsync();
    Write(Path.Combine(output, "expected"), scenario.Name + ".json", JsonSerializer.Serialize(new { Release = version, Entries = seen }, new JsonSerializerOptions { WriteIndented = true }));
    foreach (var transient in new[] { ".toucan/recovery" })
        if (Directory.Exists(Path.Combine(root, transient))) Directory.Delete(Path.Combine(root, transient), true);
}

// User settings the way this release writes them, without touching the real settings folder.
var settings = new Toucan.Core.Options.AppOptions { PageSize = 250, RecentProjectsLimit = 7 };
Write(output, "user/settings.json", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

static ServiceProvider BuildProvider()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddToucanCore();
    services.AddToucanDefaults();
    // Desktop-only services: the app registers real ones; a save does not need them.
    services.AddSingleton(Substitute.For<IFileWatcherService>());
    services.AddSingleton(Substitute.For<IAutoSaveService>());
    services.AddSingleton(Substitute.For<Toucan.Core.Contracts.IRecentProjectService>());
    // Older releases left some Core services to the app: register each missing one that has a single Core implementation.
    var core = typeof(ProjectLifecycleService).Assembly;
    foreach (var contract in core.GetTypes().Where(t => t.IsInterface && t.Namespace == "Toucan.Core.Contracts.Services"))
    {
        if (services.Any(d => d.ServiceType == contract)) continue;
        var impl = core.GetTypes().Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && contract.IsAssignableFrom(t) && t.IsPublic).ToList();
        if (impl.Count == 1) ((ICollection<ServiceDescriptor>)services).Add(new ServiceDescriptor(contract, impl[0], ServiceLifetime.Singleton));
        else if (impl.Count == 0) ((ICollection<ServiceDescriptor>)services).Add(new ServiceDescriptor(contract, Substitute.For([contract], [])));
    }
    return services.BuildServiceProvider();
}

// The desktop app builds the lifecycle service with its own watcher, autosave and recent-projects services; stand-ins do here.
static IProjectLifecycleService Lifecycle(IServiceProvider provider)
{
    var ctor = typeof(ProjectLifecycleService).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
    var args = ctor.GetParameters().Select(p =>
    {
        var service = provider.GetService(p.ParameterType);
        if (service is not null) return service;
        if (p.ParameterType.IsInterface && !p.HasDefaultValue) return Substitute.For([p.ParameterType], []);
        if (!p.HasDefaultValue && p.ParameterType.IsClass && !p.ParameterType.IsAbstract) return ActivatorUtilities.CreateInstance(provider, p.ParameterType);
        return p.HasDefaultValue ? p.DefaultValue : null;
    }).ToArray();
    return (IProjectLifecycleService)ctor.Invoke(args);
}
