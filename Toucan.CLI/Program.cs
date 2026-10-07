using Toucan.Modules;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Plugins;

namespace Toucan.CLI;

/// <summary>
/// Toucan CLI — command-line interface for i18n translation management.
/// Designed for CI/CD pipelines and AI agent integration.
///
/// Commands:
///   toucan check [folder]        — Run validation rules, exit code 1 on errors
///   toucan stats [folder]        — Print translation progress per language
///   toucan translate [folder]    — Batch pre-translate untranslated items
///   toucan export [folder] -f fmt — Export to a different format
///   toucan list-formats          — List supported formats
///   toucan list-keys [folder]    — List all translation keys (for AI tools)
///   toucan get [folder] [key]    — Get value for a key across languages (JSON output)
///   toucan set [folder] [key] [lang] [value] — Set a single translation
///   toucan plugins list|trust|revoke|enable|disable [id] — Manage plugins
/// </summary>
internal static class Program
{
    /// <summary>Plugin IDs from <c>--allow-plugin</c>: trusted for this run only, nothing is saved.</summary>
    private static readonly HashSet<string> s_allowedPlugins = new(StringComparer.OrdinalIgnoreCase);

    private static readonly FilePluginPolicyStore s_policy = new(
        Environment.GetEnvironmentVariable("TOUCAN_PLUGIN_POLICY") is { Length: > 0 } policyFile ? policyFile : FilePluginPolicyStore.DefaultPath());

    // Same composition root as the GUI (AddToucanCore), so every registered format, provider and rule is available here.
    // Plugins load only if enabled and already trusted (in the GUI or with `toucan plugins trust`): a CI run never prompts.
    private static readonly Lazy<ServiceProvider> s_services = new(() =>
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToucanCore();
        services.AddToucanDefaults();

        var options = new PluginHostOptions { Policy = s_policy };
        options.Roots.Add(Environment.GetEnvironmentVariable("TOUCAN_PLUGINS_DIR") is { Length: > 0 } dir ? dir : PluginHostOptions.DefaultRoot());
        foreach (var id in s_allowedPlugins) options.AllowForThisRun.Add(id);
        services.AddToucanPlugins(options);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    });

    private static T Get<T>() where T : notnull => s_services.Value.GetRequiredService<T>();

    private static int Main(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 0; }

        var command = args[0].ToLowerInvariant();
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == "--allow-plugin") s_allowedPlugins.Add(args[i + 1]);

        if (command == "plugins") return RunPlugins(args);

        var folder = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : Directory.GetCurrentDirectory();

        try
        {
            var code = Dispatch(command, folder, args);
            WarnAboutPendingPlugins();
            return code;
        }
        catch (FormatUnavailableException ex)
        {
            Console.Error.WriteLine(ex.Message);
            WarnAboutPendingPlugins();
            return 1;
        }
    }

    private static int Dispatch(string command, string folder, string[] args)
    {
        return command switch
        {
            "check" => RunCheck(folder),
            "stats" => RunStats(folder),
            "translate" => RunTranslate(folder, args),
            "export" => RunExport(folder, args),
            "list-formats" => RunListFormats(),
            "list-keys" => RunListKeys(folder),
            "get" => RunGet(folder, args),
            "set" => RunSet(folder, args),
            "--help" or "-h" or "help" => PrintUsage(),
            _ => Error($"Unknown command: {command}")
        };
    }

    // --- plugins ---

    private static IReadOnlyList<PluginLoadResult> Plugins => Get<IPluginCatalog>().Plugins;

    private static void WarnAboutPendingPlugins()
    {
        if (!s_services.IsValueCreated) return;
        var pending = Plugins.Where(p => p.Status == PluginStatus.NeedsTrust).Select(p => p.DisplayId).ToList();
        if (pending.Count > 0)
            Console.Error.WriteLine($"Note: {pending.Count} plugin(s) not loaded because they are not trusted: {string.Join(", ", pending)}. Run 'toucan plugins list'.");
    }

    private static int RunPlugins(string[] args)
    {
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
        var id = args.Length > 2 && !args[2].StartsWith('-') ? args[2] : null;

        switch (sub)
        {
            case "list":
                return ListPlugins();
            case "trust" or "revoke" or "enable" or "disable":
                if (id is null) return Error($"Usage: toucan plugins {sub} <id>");
                return ChangePluginPolicy(sub, id);
            default:
                return Error($"Unknown plugins command: {sub}. Use list, trust, revoke, enable or disable.");
        }
    }

    private static int ListPlugins()
    {
        Console.WriteLine("Built-in modules (always on):");
        foreach (var m in Get<IPluginCatalog>().BuiltInModules)
            Console.WriteLine($"  {m.Id}{(m.Version.Length > 0 ? $" {m.Version}" : string.Empty)}: {string.Join(", ", m.Registered)}");
        Console.WriteLine();

        if (Plugins.Count == 0)
        {
            Console.WriteLine("No plugins found.");
            return 0;
        }

        foreach (var p in Plugins)
        {
            var version = p.Manifest?.Version is { Length: > 0 } v ? $" {v}" : string.Empty;
            Console.WriteLine($"{p.DisplayId}{version}");
            Console.WriteLine($"  status:    {p.Status}{(p.Status == PluginStatus.NeedsTrust ? " (not loaded)" : string.Empty)}");
            Console.WriteLine($"  signature: {(p.Signature == PluginSignatureStatus.NotSigned ? "not signed" : p.Signature.ToString().ToLowerInvariant())}");
            if (p.ContentHash is { Length: > 0 } hash) Console.WriteLine($"  sha256:    {hash}");
            if (p.Registered is { Count: > 0 } reg) Console.WriteLine($"  provides:  {string.Join(", ", reg)}");
            if (p.Error is { Length: > 0 } err) Console.WriteLine($"  note:      {err}");
            Console.WriteLine($"  folder:    {p.Directory}");
        }

        if (Plugins.Any(p => p.Status == PluginStatus.NeedsTrust))
            Console.WriteLine("\nTrust a plugin only if you trust its author: it runs with your permissions.\n  toucan plugins trust <id>");
        return 0;
    }

    private static int ChangePluginPolicy(string action, string id)
    {
        var plugin = Plugins.FirstOrDefault(p => string.Equals(p.DisplayId, id, StringComparison.OrdinalIgnoreCase));

        switch (action)
        {
            case "trust":
                if (plugin?.Manifest is null || plugin.ContentHash is null)
                    return Error($"No readable plugin '{id}' found. Run 'toucan plugins list'.");
                if (plugin.Status == PluginStatus.Rejected)
                    return Error($"Plugin '{id}' was rejected and cannot be trusted: {plugin.Error}");
                s_policy.Trust(plugin.Manifest.Id, plugin.ContentHash);
                Console.WriteLine($"Trusted {plugin.Manifest.Id} {plugin.Manifest.Version} (sha256 {plugin.ContentHash[..12]}…). It will load on the next run.");
                return 0;
            case "revoke":
                s_policy.Revoke(plugin?.Manifest?.Id ?? id);
                Console.WriteLine($"Revoked trust for {id}.");
                return 0;
            default:
                s_policy.SetEnabled(plugin?.Manifest?.Id ?? id, action == "enable");
                Console.WriteLine($"{(action == "enable" ? "Enabled" : "Disabled")} {id}.");
                return 0;
        }
    }

    private static int RunCheck(string folder)
    {
        var (settings, translations) = LoadProject(folder);
        if (translations.Count == 0) { Console.Error.WriteLine("No translations found."); return 1; }

        var pipeline = Get<IValidationPipeline>();
        var ctx = new ValidationContext { Items = translations, PrimaryLanguage = settings.PrimaryLanguage };
        var results = pipeline.RunAll(ctx).ToList();

        var errors = results.Where(r => r.Severity == ValidationSeverity.Error).ToList();
        var warnings = results.Where(r => r.Severity == ValidationSeverity.Warning).ToList();

        foreach (var r in results.OrderBy(r => r.Severity))
            Console.WriteLine($"[{r.Severity}] {r.Language ?? "*"}/{r.Namespace}: {r.Message}");

        Console.WriteLine($"\n{errors.Count} error(s), {warnings.Count} warning(s), {results.Count - errors.Count - warnings.Count} info(s)");
        return errors.Count > 0 ? 1 : 0;
    }

    private static int RunStats(string folder)
    {
        var (settings, translations) = LoadProject(folder);
        if (translations.Count == 0) { Console.Error.WriteLine("No translations found."); return 1; }

        var primary = settings.PrimaryLanguage;
        var primaryKeys = translations.Where(t => t.Language == primary).Select(t => t.Namespace).ToHashSet();
        var languages = translations.Select(t => t.Language).Distinct().OrderBy(l => l).ToList();

        Console.WriteLine($"Project: {settings.Name ?? Path.GetFileName(folder)}");
        Console.WriteLine($"Primary: {primary}");
        Console.WriteLine($"Keys: {primaryKeys.Count}");
        Console.WriteLine($"Languages: {languages.Count}");
        Console.WriteLine();

        foreach (var lang in languages)
        {
            var total = translations.Count(t => t.Language == lang);
            var filled = translations.Count(t => t.Language == lang && !string.IsNullOrEmpty(t.Value));
            var pct = total == 0 ? 0 : filled * 100 / total;
            var bar = new string('█', pct / 5) + new string('░', 20 - pct / 5);
            Console.WriteLine($"  {lang,-8} {bar} {pct,3}% ({filled}/{total})");
        }
        return 0;
    }

    private static int RunExport(string folder, string[] args)
    {
        var formatArg = GetArg(args, "-f") ?? GetArg(args, "--format") ?? "json";
        var strategies = Get<ITranslationStrategyFactory>().SaveStrategies;
        // Accept a format ID ("android-xml") or the legacy enum name ("AndroidXml").
        var formatId = Enum.TryParse<SaveStyles>(formatArg, true, out var legacyStyle) ? FormatIds.FromStyle(legacyStyle) : formatArg;
        var strategy = strategies.FirstOrDefault(s => FormatIds.Comparer.Equals(s.FormatId, formatId));
        if (strategy == null)
        {
            Console.Error.WriteLine($"Unknown format: {formatArg}. Use 'toucan list-formats'.");
            return 1;
        }

        var outputDir = GetArg(args, "-o") ?? GetArg(args, "--output") ?? Path.Combine(folder, "export");
        var (_, translations) = LoadProject(folder);
        if (translations.Count == 0) { Console.Error.WriteLine("No translations found."); return 1; }

        Directory.CreateDirectory(outputDir);
        var ctx = new SaveContext
        {
            LanguageDictionary = translations.GroupBy(t => t.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
            NsTreeItems = [],
            Languages = translations.Select(t => t.Language).Distinct().ToList()
        };
        strategy.Save(outputDir, ctx);
        Console.WriteLine($"Exported {translations.Count} items to {outputDir} ({strategy.FormatId})");
        return 0;
    }

    private static int RunListFormats()
    {
        Console.WriteLine("Supported formats:");
        foreach (var s in Get<ITranslationStrategyFactory>().SaveStrategies)
            Console.WriteLine($"  {s.FormatId,-16} {s.DisplayName}");
        return 0;
    }

    private static int RunListKeys(string folder)
    {
        var (settings, translations) = LoadProject(folder);
        var keys = translations.Select(t => t.Namespace).Distinct().OrderBy(k => k);
        foreach (var key in keys)
            Console.WriteLine(key);
        return 0;
    }

    private static int RunGet(string folder, string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("Usage: toucan get [folder] [key]"); return 1; }
        var key = args[2];
        var (_, translations) = LoadProject(folder);
        var matches = translations.Where(t => t.Namespace == key).ToList();
        if (matches.Count == 0) { Console.Error.WriteLine($"Key not found: {key}"); return 1; }

        Console.WriteLine("{");
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var comma = i < matches.Count - 1 ? "," : "";
            Console.WriteLine($"  \"{m.Language}\": \"{Escape(m.Value)}\"{comma}");
        }
        Console.WriteLine("}");
        return 0;
    }

    private static int RunSet(string folder, string[] args)
    {
        if (args.Length < 5) { Console.Error.WriteLine("Usage: toucan set [folder] [key] [lang] [value]"); return 1; }
        var key = args[2];
        var lang = args[3];
        var value = args[4];

        var (settings, translations) = LoadProject(folder);
        var item = translations.FirstOrDefault(t => t.Namespace == key && t.Language == lang);
        if (item != null)
            item.Value = value;
        else
            translations.Add(new TranslationItem { Namespace = key, Language = lang, Value = value });

        // Save back
        var strategy = Get<ITranslationStrategyFactory>().GetSaveStrategy(settings.SaveFormat)
            ?? throw new FormatUnavailableException(settings.SaveFormat);
        var ctx = new SaveContext
        {
            LanguageDictionary = translations.GroupBy(t => t.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
            NsTreeItems = [],
            Languages = translations.Select(t => t.Language).Distinct().ToList()
        };
        strategy.Save(folder, ctx);
        Console.WriteLine($"Set {lang}/{key} = \"{value}\"");
        return 0;
    }

    private static int RunTranslate(string folder, string[] args)
    {
        var providerName = GetArg(args, "-p") ?? GetArg(args, "--provider") ?? "mock";
        var targetLang = GetArg(args, "-l") ?? GetArg(args, "--lang");
        var overwrite = args.Contains("--overwrite");
        var dryRun = args.Contains("--dry-run");

        var (settings, translations) = LoadProject(folder);
        if (translations.Count == 0) { Console.Error.WriteLine("No translations found."); return 1; }

        var primary = settings.PrimaryLanguage;
        var sourceItems = translations.Where(t => t.Language == primary && !string.IsNullOrEmpty(t.Value)).ToList();
        if (sourceItems.Count == 0) { Console.Error.WriteLine($"No source translations in primary language ({primary})."); return 1; }

        // Determine target languages
        var allLangs = translations.Select(t => t.Language).Distinct().Where(l => l != primary).ToList();
        List<string> targetLangs = targetLang != null ? [targetLang] : allLangs;

        // Build jobs: untranslated items (or all if --overwrite)
        var jobs = new List<PretranslationJob>();
        foreach (var lang in targetLangs)
        {
            foreach (var src in sourceItems)
            {
                var existing = translations.FirstOrDefault(t => t.Language == lang && t.Namespace == src.Namespace);
                if (!overwrite && existing != null && !string.IsNullOrEmpty(existing.Value)) continue;
                jobs.Add(new PretranslationJob(src.Namespace, src.Value, primary, lang));
            }
        }

        if (jobs.Count == 0) { Console.WriteLine("Nothing to translate — all items are already filled."); return 0; }
        Console.WriteLine($"Translating {jobs.Count} items using '{providerName}' provider...");

        // Resolve provider
        var provider = FindProvider(providerName);
        if (provider == null)
        {
            var available = string.Join(", ", s_services.Value.GetServices<ITranslationProvider>().Select(p => p.Name.ToLowerInvariant()));
            Console.Error.WriteLine($"Unknown provider: {providerName}. Available: {available}");
            return 1;
        }

        // Run translation
        var options = new PretranslationOptions
        {
            Overwrite = overwrite,
            PreviewOnly = dryRun,
            // The AI provider uses the project's own prompt and context; other providers ignore these.
            ProviderOptions = new Dictionary<string, string>
            {
                ["project_path"] = Path.GetFullPath(folder),
                ["context"] = settings.Context ?? string.Empty,
            },
        };
        var progress = new Progress<PretranslationProgress>(p =>
            Console.Write($"\r  [{p.Completed}/{p.Total}]"));

        var results = provider.PretranslateAsync(jobs, options, progress, CancellationToken.None).GetAwaiter().GetResult().ToList();
        Console.WriteLine();

        var succeeded = results.Count(r => r.Succeeded);
        var failed = results.Count(r => !r.Succeeded);
        Console.WriteLine($"  {succeeded} translated, {failed} failed");
        // Say why, once per distinct reason ("AI is turned off…", "HTTP 401 …"), so a failed run is actionable.
        foreach (var reason in results.Where(r => !r.Succeeded).GroupBy(r => r.ErrorMessage ?? "Unknown error").Take(3))
            Console.Error.WriteLine($"  {reason.Count()} × {reason.Key}");

        if (dryRun)
        {
            Console.WriteLine("\n  --dry-run: no files written. Preview:");
            foreach (var r in results.Where(r => r.Succeeded).Take(10))
                Console.WriteLine($"    {r.Language}/{r.Namespace}: \"{r.TranslatedValue}\"");
            if (succeeded > 10) Console.WriteLine($"    ... and {succeeded - 10} more");
            return 0;
        }

        // Apply results
        foreach (var r in results.Where(r => r.Succeeded && r.TranslatedValue != null))
        {
            var item = translations.FirstOrDefault(t => t.Language == r.Language && t.Namespace == r.Namespace);
            if (item != null)
                item.Value = r.TranslatedValue!;
            else
                translations.Add(new TranslationItem { Namespace = r.Namespace, Language = r.Language, Value = r.TranslatedValue! });
        }

        // Save
        var strategy = Get<ITranslationStrategyFactory>().GetSaveStrategy(settings.SaveFormat)
            ?? throw new FormatUnavailableException(settings.SaveFormat);
        var ctx = new SaveContext
        {
            LanguageDictionary = translations.GroupBy(t => t.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
            NsTreeItems = [],
            Languages = translations.Select(t => t.Language).Distinct().ToList()
        };
        strategy.Save(folder, ctx);
        Console.WriteLine($"  Saved to {folder}");
        return failed > 0 ? 1 : 0;
    }

    private static ITranslationProvider? FindProvider(string name)
    {
        // claude, openai and gemini were providers before AI Integration; they are the "ai" provider now.
        var current = Toucan.Core.Services.Ai.LegacyAiMigration.CurrentProviderName(name);
        return s_services.Value.GetServices<ITranslationProvider>()
            .FirstOrDefault(p => string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase));
    }

    // --- Helpers ---

    private static (ProjectSettings, List<TranslationItem>) LoadProject(string folder)
    {
        var result = Get<IProjectService>().LoadProject(folder);
        return (result.Settings, result.Translations);
    }

    private static string? GetArg(string[] args, string flag)
    {
        var idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    private static int Error(string msg) { Console.Error.WriteLine(msg); return 1; }

    private static int PrintUsage()
    {
        Console.WriteLine("""
            Toucan CLI — i18n translation management

            Usage: toucan <command> [folder] [options]

            Commands:
              check [folder]              Run validation, exit 1 on errors (CI/CD)
              stats [folder]              Print translation progress per language
              translate [folder]          Batch pre-translate untranslated items
              export [folder] -f <format> Export translations to another format
              list-formats                List all supported file formats
              list-keys [folder]          List all translation keys (one per line)
              get [folder] <key>          Get translations for a key (JSON)
              set [folder] <key> <lang> <value>  Set a single translation
              plugins [list]              Show installed plugins and whether they are trusted
              plugins trust <id>          Trust a plugin's current files (it then loads)
              plugins revoke|enable|disable <id>

            Plugins:
              Plugins live in Documents/Toucan/plugins (override: TOUCAN_PLUGINS_DIR). Only enabled, trusted
              plugins load; the CLI never prompts. Decisions are shared with the app (override the file with
              TOUCAN_PLUGIN_POLICY). --allow-plugin <id> trusts a plugin for one run without saving anything.

            Translate options:
              -p, --provider <name>  Provider: mock, google, deepl, microsoft, ai, custom
              -l, --lang <code>      Target language (default: all non-primary)
              --overwrite            Overwrite existing translations
              --dry-run              Preview without saving

            AI:
              The "ai" provider uses AI Integration: AI must be turned on in the app (Settings → AI), or set
              TOUCAN_AI_BACKEND (anthropic, openai, gemini) for one run. The key comes from the app's secret store
              or ANTHROPIC_API_KEY / OPENAI_API_KEY / GEMINI_API_KEY. A project's .toucan/prompts/translate.md is used.

            Export options:
              -f, --format <fmt>   Target format for export
              -o, --output <dir>   Output directory for export

            Examples:
              toucan check ./locales
              toucan stats .
              toucan translate . -p google -l fr-FR
              toucan translate . -p deepl --dry-run
              toucan export . -f Yaml -o ./out
              toucan get . app.title
              toucan set . app.title fr-FR "Mon Application"
            """);
        return 0;
    }
}
