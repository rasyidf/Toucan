using System.IO;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services;

public class ProjectService(
    IFileService fileService,
    IEnumerable<ISaveStrategy> saveStrategies,
    ITranslationStrategyFactory strategyFactory,
    IProjectModeResolver modeResolver,
    ILogger<ProjectService> logger) : IProjectService
{
    public ProjectService(IFileService fileService, IEnumerable<ISaveStrategy> saveStrategies)
        : this(fileService, saveStrategies,
            new TranslationStrategyFactory(saveStrategies, new List<ILoadStrategy>()),
            new ProjectModeResolver(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectService>.Instance)
    { }

    public ProjectLoadResult LoadProject(string folder, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        using var _ = ScanContext.Begin(ct, progress);
        ct.ThrowIfCancellationRequested();
        // Try loading project settings from toucan.project manifest
        var settings = ProjectSettings.LoadFrom(folder);
        if (settings == null)
        {
            var babelFiles = Directory.GetFiles(folder, "*.babel");
            if (babelFiles.Length > 1) throw new FormatException("This folder has multiple BabelEdit projects. Import each project in its own folder.");
            if (babelFiles.Length == 1) settings = BabelProjectReader.Read(babelFiles[0]);
            else
            {
                settings = ProjectSettings.CreateDefault(folder);
                settings.SaveFormat = new FormatDetector(strategyFactory.SaveStrategies).Detect(folder);
            }
        }
        settings.ProjectPath = folder;
        settings.DefaultPathResolver = ResolveDefaultPath;

        // Load translations using the detected/configured format
        var translations = settings.BabelImport != null && settings.SaveFormat == FormatIds.Json
            ? LoadBabelJson(settings) : Load(folder, settings.SaveFormat, settings.BabelImport == null, settings.InputEncoding);
        BabelProjectReader.ApplyMetadata(settings, translations);
        // Apply language aliases
        if (settings.LanguageAliases is { Count: > 0 })
            foreach (var t in translations)
                if (settings.LanguageAliases.TryGetValue(t.Language, out var mapped))
                    t.Language = mapped;

        var states = settings.TranslationStates.ToDictionary(s => (s.Language, s.Key));
        foreach (var item in translations)
            if (states.TryGetValue((item.Language, item.Namespace), out var state))
            {
                item.Comment = state.Comment;
                item.IsApproved = state.Approved;
                foreach (var entry in state.Data) (item.FormatData ??= new())[entry.Key] = entry.Value;
            }

        // Backfill settings from loaded data if manifest was missing
        if (settings.Languages.Count == 0)
            settings.Languages = translations.ToLanguages().ToList();

        var warnings = new List<string>();
        var strategy = strategyFactory.GetSaveStrategy(settings.SaveFormat);
        var unsupported = strategy?.FindUnsupportedConstructs(folder) ?? [];
        if (unsupported.Count > 0)
            warnings.Add($"These {strategy!.DisplayName} files contain content Toucan cannot write back: {string.Join("; ", unsupported)}. "
                + "You can edit and review, but saving is blocked to protect your files. Use Save As to write a copy.");

        return new ProjectLoadResult { Settings = settings, Translations = translations, Warnings = warnings };
    }

    public ProjectSettings CreateProject(string folder, IEnumerable<string> languages, string formatId = FormatIds.Json, bool createManifest = true, string? name = null)
    {
        Directory.CreateDirectory(folder);

        var langList = languages.ToList();
        foreach (var language in langList)
            CreateLanguage(folder, language, formatId);

        var settings = new ProjectSettings
        {
            Name = name ?? Path.GetFileName(folder),
            ProjectPath = folder,
            PrimaryLanguage = langList.FirstOrDefault() ?? "en-US",
            Languages = langList,
            SaveFormat = formatId,
            DefaultPathResolver = ResolveDefaultPath
        };

        if (createManifest)
            settings.Save();

        return settings;
    }

    public void Save(ProjectSettings project, List<NsTreeItem> items, IEnumerable<TranslationItem> translations)
    {
        var toSave = translations.ToList();
        if (project.BabelImport != null && project.SaveFormat == FormatIds.Json)
            foreach (var item in toSave.Where(i => i.FormatData?.ContainsKey("json.file") != true))
            {
                var urls = project.TranslationPackages.SelectMany(p => p.TranslationUrls.Select(u => (Package: p.Name, Url: u)))
                    .Where(p => p.Url.Language == item.Language).ToList();
                if (urls.Count != 1) throw new InvalidOperationException("Choose a package before saving a new key in a multi-package BabelEdit import.");
                var data = item.FormatData ??= new();
                data["json.file"] = urls[0].Url.Path;
                data["babel.package"] = urls[0].Package;
                data["babel.key"] = item.Namespace;
            }
        if (project.BabelImport != null && project.SaveFormat == FormatIds.Json)
            foreach (var item in toSave)
            {
                var data = item.FormatData!;
                var prefix = data.GetValueOrDefault("babel.package") + ".";
                data["babel.key"] = project.TranslationPackages.Count > 1 && item.Namespace.StartsWith(prefix, StringComparison.Ordinal)
                    ? item.Namespace[prefix.Length..] : item.Namespace;
            }
        // Reverse alias mapping for file output
        if (project.LanguageAliases is { Count: > 0 })
        {
            // Build reverse map safely (last alias wins if multiple map to same file code)
            var reverse = new Dictionary<string, string>();
            foreach (var kv in project.LanguageAliases)
                reverse[kv.Value] = kv.Key;

            var list = toSave;
            foreach (var t in list)
                if (reverse.TryGetValue(t.Language, out var fileCode))
                    t.Language = fileCode;
            toSave = list;
        }

        try
        {
            Save(project.ProjectPath, project.SaveFormat, items, toSave, project.SaveEmptyTranslations ?? true, project.BabelImport?.JsonLayout);
            if (FormatIds.TryGetStyle(project.SaveFormat, out _) && (project.TextEncoding != null || project.LineEnding != null))
            {
                var files = toSave.ToLanguages().SelectMany(lang => GetLanguageFiles(project, lang));
                foreach (var file in files.Distinct(StringComparer.Ordinal))
                {
                    var utf8Properties = toSave.Any(i => i.FormatData?.GetValueOrDefault("properties.encoding") == "UTF-8" &&
                        Path.GetFullPath(Path.Combine(project.ProjectPath, i.FormatData.GetValueOrDefault("properties.file") ?? i.Language + ".properties")) == Path.GetFullPath(file));
                    ProjectTextFormat.Apply(file, project.TextEncoding, project.LineEnding,
                        project.SaveFormat == FormatIds.JavaProperties && !utf8Properties ? System.Text.Encoding.Latin1 : null);
                }
            }
        }
        finally
        {
            // Restore display codes for in-memory state, also when the write failed
            if (project.LanguageAliases is { Count: > 0 })
                foreach (var t in (IEnumerable<TranslationItem>)toSave)
                    if (project.LanguageAliases.TryGetValue(t.Language, out var mapped))
                        t.Language = mapped;
        }

        BabelProjectReader.CaptureMetadata(project, toSave);
        project.TranslationStates = toSave.GroupBy(i => (i.Language, i.Namespace)).Select(g => g.First())
            .Select(i => new TranslationState { Language = i.Language, Key = i.Namespace, Comment = i.Comment, Approved = i.IsApproved,
                Data = i.FormatData?.Where(p => p.Key.StartsWith("babel.", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value) ?? [] }).ToList();
        // Update project manifest with current language list
        project.Languages = toSave.ToLanguages().ToList();
        project.Save();
    }

    public void CreateLanguage(string folder, string language, string formatId = FormatIds.Json)
    {
        var translations = new List<TranslationItem>
        {
            new() { Namespace = "app", Value = string.Empty, Language = language }
        };

        var context = new SaveContext
        {
            LanguageDictionary = new Dictionary<string, IEnumerable<TranslationItem>> { { language, translations } },
            NsTreeItems = [],
            Languages = [language]
        };

        var strategy = strategyFactory.GetSaveStrategy(formatId);
        if (strategy != null)
            strategy.Save(folder, context);
        else if (!FormatIds.TryGetStyle(formatId, out _))
            throw new FormatUnavailableException(formatId);
        else
            fileService.Save(folder, language + ".json", new Dictionary<string, string> { { "app", "" } });
    }

    public List<TranslationItem> Load(string folder)
    {
        return Load(folder, FormatIds.Json);
    }

    private List<TranslationItem> Load(string folder, string formatId, bool useManifest = true, string? inputEncoding = null)
    {
        if (string.IsNullOrEmpty(folder)) return [];

        var strategy = strategyFactory.GetLoadStrategy(formatId);

        // A format we know nothing about (e.g. its plugin is missing) must not fall through to the manifest or
        // JSON loaders: they would misread the files, and a later save could overwrite them.
        // Built-in formats without a loader keep the legacy JSON fallback below.
        if (strategy == null && !FormatIds.TryGetStyle(formatId, out _))
            throw new FormatUnavailableException(formatId);

        var variant = modeResolver.Resolve(folder);
        if (useManifest && formatId == FormatIds.Json && variant == ProjectTypeVariant.ConfigManifest)
        {
            var loader = strategyFactory.GetManifestLoadStrategy();
            if (loader != null)
            {
                var items = loader.Load(folder).ToList();
                if (items.Count > 0) return items;
            }
        }

        // Use the detected format's load strategy (not just Json fallback)
        if (strategy != null) return strategy.Load(folder, inputEncoding).ToList();

        // Final fallback: try Json
        return (strategyFactory.GetLoadStrategy(FormatIds.Json)?.Load(folder) ?? []).ToList();
    }

    public void Save(string path, string formatId, List<NsTreeItem> items, IEnumerable<TranslationItem> translations)
        => Save(path, formatId, items, translations, true, null);

    private void Save(string path, string formatId, List<NsTreeItem> items, IEnumerable<TranslationItem> translations, bool saveEmpty, string? jsonLayout)
    {
        var strategy = strategyFactory.GetSaveStrategy(formatId)
            ?? throw new FormatUnavailableException(formatId);

        // Saving rewrites the files; refuse when that would silently drop content the format cannot write back.
        if (Directory.Exists(path) && strategy.FindUnsupportedConstructs(path) is { Count: > 0 } unsafeConstructs)
            throw new FormatSaveBlockedException(formatId, unsafeConstructs);

        var context = new SaveContext
        {
            SaveEmptyTranslations = saveEmpty,
            JsonLayout = jsonLayout,
            LanguageDictionary = translations.ToLanguageDictionary(),
            NsTreeItems = items ?? [],
            Languages = translations.ToLanguages().ToList()
        };

        strategy.Save(path, context);
    }

    public string GetDefaultFilePath(ProjectSettings settings, string language) =>
        settings.LanguageFilePaths?.TryGetValue(language, out var custom) == true
            ? custom
            : ResolveDefaultPath(settings.SaveFormat, language);

    public IReadOnlyList<string> GetLanguageFiles(ProjectSettings settings, string language)
    {
        if (settings.BabelImport != null)
            return settings.TranslationPackages.SelectMany(p => p.TranslationUrls).Where(u => u.Language == language)
                .Select(u => BabelProjectReader.ResolvePath(settings.ProjectPath, u.Path)).Distinct(StringComparer.Ordinal).ToList();
        if (settings.LanguageFilePaths?.TryGetValue(language, out var custom) == true)
            return [Path.IsPathRooted(custom) ? custom : Path.Combine(settings.ProjectPath, custom.Replace('/', Path.DirectorySeparatorChar))];

        var strategy = strategyFactory.GetSaveStrategy(settings.SaveFormat);
        return strategy?.LanguageFiles(settings.ProjectPath, language)
            ?? [Path.Combine(settings.ProjectPath, language + ".json")];
    }

    private string ResolveDefaultPath(string formatId, string language) =>
        strategyFactory.GetSaveStrategy(formatId)?.DefaultFilePath(language) ?? $"{language}.json";

    private static List<TranslationItem> LoadBabelJson(ProjectSettings settings)
    {
        var result = new List<TranslationItem>();
        foreach (var package in settings.TranslationPackages)
            foreach (var url in package.TranslationUrls)
            {
                var path = BabelProjectReader.ResolvePath(settings.ProjectPath, url.Path);
                var content = File.ReadAllText(path);
                using var valid = System.Text.Json.JsonDocument.Parse(content);
                foreach (var item in NestedJsonParser.ParseNestedJson(url.Language, content))
                {
                    var data = item.FormatData ??= new();
                    data["json.file"] = url.Path;
                    data["babel.package"] = package.Name;
                    data["babel.key"] = item.Namespace;
                    if (settings.TranslationPackages.Count > 1) item.Namespace = package.Name + "." + item.Namespace;
                    result.Add(item);
                }
            }
        return result;
    }
}
