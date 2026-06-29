using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;
using Toucan.Core.Services.LoadStrategies;
using Toucan.Core.Services.SaveStrategies;
using Toucan.Core.Services.Validation;

namespace Toucan.CLI;

/// <summary>
/// Toucan CLI — command-line interface for i18n translation management.
/// Designed for CI/CD pipelines and AI agent integration.
///
/// Commands:
///   toucan check [folder]        — Run validation rules, exit code 1 on errors
///   toucan stats [folder]        — Print translation progress per language
///   toucan export [folder] -f fmt — Export to a different format
///   toucan list-formats          — List supported formats
///   toucan list-keys [folder]    — List all translation keys (for AI tools)
///   toucan get [folder] [key]    — Get value for a key across languages (JSON output)
///   toucan set [folder] [key] [lang] [value] — Set a translation value
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 0; }

        var command = args[0].ToLowerInvariant();
        var folder = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : Directory.GetCurrentDirectory();

        return command switch
        {
            "check" => RunCheck(folder),
            "stats" => RunStats(folder),
            "export" => RunExport(folder, args),
            "list-formats" => RunListFormats(),
            "list-keys" => RunListKeys(folder),
            "get" => RunGet(folder, args),
            "set" => RunSet(folder, args),
            "--help" or "-h" or "help" => PrintUsage(),
            _ => Error($"Unknown command: {command}")
        };
    }

    private static int RunCheck(string folder)
    {
        var (settings, translations) = LoadProject(folder);
        if (translations.Count == 0) { Console.Error.WriteLine("No translations found."); return 1; }

        var rules = new IValidationRule[]
        {
            new MissingTranslationRule(),
            new PlaceholderMismatchRule(),
            new DuplicateKeyRule(),
            new EmptyValueRule(),
            new UntranslatedCopyRule(),
            new WhitespaceMismatchRule()
        };
        var pipeline = new ValidationPipeline(rules);
        var ctx = new ValidationContext { Items = translations, Settings = settings };
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
        if (!Enum.TryParse<SaveStyles>(formatArg, true, out var style))
        {
            Console.Error.WriteLine($"Unknown format: {formatArg}. Use 'toucan list-formats'.");
            return 1;
        }

        var outputDir = GetArg(args, "-o") ?? GetArg(args, "--output") ?? Path.Combine(folder, "export");
        var (_, translations) = LoadProject(folder);
        if (translations.Count == 0) { Console.Error.WriteLine("No translations found."); return 1; }

        var strategies = CreateSaveStrategies();
        var strategy = strategies.FirstOrDefault(s => s.Style == style);
        if (strategy == null) { Console.Error.WriteLine($"No save strategy for: {style}"); return 1; }

        Directory.CreateDirectory(outputDir);
        var ctx = new SaveContext
        {
            LanguageDictionary = translations.GroupBy(t => t.Language).ToDictionary(g => g.Key, g => (IEnumerable<TranslationItem>)g.ToList()),
            NsTreeItems = [],
            Languages = translations.Select(t => t.Language).Distinct().ToList()
        };
        strategy.Save(outputDir, ctx);
        Console.WriteLine($"Exported {translations.Count} items to {outputDir} ({style})");
        return 0;
    }

    private static int RunListFormats()
    {
        Console.WriteLine("Supported formats:");
        foreach (var s in Enum.GetValues<SaveStyles>())
            Console.WriteLine($"  {s}");
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
        var strategies = CreateSaveStrategies();
        var strategy = strategies.FirstOrDefault(s => s.Style == settings.SaveStyle) ?? strategies.First();
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

    // --- Helpers ---

    private static (ProjectSettings, List<TranslationItem>) LoadProject(string folder)
    {
        var fileService = new FileService(Microsoft.Extensions.Logging.Abstractions.NullLogger<FileService>.Instance);
        var loadStrategies = CreateLoadStrategies(fileService);
        var saveStrategies = CreateSaveStrategies();
        var factory = new TranslationStrategyFactory(saveStrategies, loadStrategies);
        var resolver = new ProjectModeResolver();
        var svc = new ProjectService(fileService, saveStrategies, factory, resolver, Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectService>.Instance);
        var result = svc.LoadProject(folder);
        return (result.Settings, result.Translations);
    }

    private static List<ILoadStrategy> CreateLoadStrategies(IFileService fs)
    {
        var jsonLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonLoadStrategy>.Instance;
        var manifestLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ManifestLoadStrategy>.Instance;
        var jsonLoader = new JsonLoadStrategy(fs, jsonLogger);
        return
        [
            jsonLoader,
            new NamespacedLoadStrategy(jsonLoader),
            new ManifestLoadStrategy(fs, manifestLogger),
            new YamlLoadStrategy(),
            new TomlLoadStrategy(),
            new PoLoadStrategy(),
            new ResxLoadStrategy(),
            new AndroidXmlLoadStrategy(),
            new IosStringsLoadStrategy(),
            new XliffLoadStrategy(),
            new ArbLoadStrategy(),
            new CsvLoadStrategy(),
        ];
    }

    private static List<ISaveStrategy> CreateSaveStrategies()
    {
        var fs = new FileService(Microsoft.Extensions.Logging.Abstractions.NullLogger<FileService>.Instance);
        return
        [
            new JsonSaveStrategy(fs),
            new NamespacedSaveStrategy(fs),
            new YamlSaveStrategy(fs),
            new PoSaveStrategy(fs),
            new IniSaveStrategy(fs),
            new TomlSaveStrategy(fs),
            new ResxSaveStrategy(fs),
            new AndroidXmlSaveStrategy(fs),
            new IosStringsSaveStrategy(fs),
            new XliffSaveStrategy(fs),
            new ArbSaveStrategy(fs),
            new CsvSaveStrategy(fs),
        ];
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
              export [folder] -f <format> Export translations to another format
              list-formats                List all supported file formats
              list-keys [folder]          List all translation keys (one per line)
              get [folder] <key>          Get translations for a key (JSON)
              set [folder] <key> <lang> <value>  Set a single translation

            Options:
              -f, --format <fmt>   Target format for export
              -o, --output <dir>   Output directory for export

            Examples:
              toucan check ./locales
              toucan stats .
              toucan export . -f Yaml -o ./out
              toucan get . app.title
              toucan set . app.title fr-FR "Mon Application"
            """);
        return 0;
    }
}
