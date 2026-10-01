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
            settings = ProjectSettings.CreateDefault(folder);
            settings.SaveFormat = new FormatDetector(strategyFactory.SaveStrategies).Detect(folder);
        }
        settings.ProjectPath = folder;
        settings.DefaultPathResolver = ResolveDefaultPath;

        // Load translations using the detected/configured format
        var translations = Load(folder, settings.SaveFormat);

        // Apply language aliases
        if (settings.LanguageAliases is { Count: > 0 })
            foreach (var t in translations)
                if (settings.LanguageAliases.TryGetValue(t.Language, out var mapped))
                    t.Language = mapped;

        // Backfill settings from loaded data if manifest was missing
        if (settings.Languages.Count == 0)
            settings.Languages = translations.ToLanguages().ToList();

        return new ProjectLoadResult { Settings = settings, Translations = translations };
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
        var toSave = translations;
        // Reverse alias mapping for file output
        if (project.LanguageAliases is { Count: > 0 })
        {
            // Build reverse map safely (last alias wins if multiple map to same file code)
            var reverse = new Dictionary<string, string>();
            foreach (var kv in project.LanguageAliases)
                reverse[kv.Value] = kv.Key;

            var list = translations.ToList();
            foreach (var t in list)
                if (reverse.TryGetValue(t.Language, out var fileCode))
                    t.Language = fileCode;
            toSave = list;
        }

        Save(project.ProjectPath, project.SaveFormat, items, toSave);

        // Restore display codes for in-memory state
        if (project.LanguageAliases is { Count: > 0 })
            foreach (var t in (IEnumerable<TranslationItem>)toSave)
                if (project.LanguageAliases.TryGetValue(t.Language, out var mapped))
                    t.Language = mapped;

        // Update project manifest with current language list
        project.Languages = translations.ToLanguages().ToList();
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

    private List<TranslationItem> Load(string folder, string formatId)
    {
        if (string.IsNullOrEmpty(folder)) return [];

        var strategy = strategyFactory.GetLoadStrategy(formatId);

        // A format we know nothing about (e.g. its plugin is missing) must not fall through to the manifest or
        // JSON loaders: they would misread the files, and a later save could overwrite them.
        // Built-in formats without a loader keep the legacy JSON fallback below.
        if (strategy == null && !FormatIds.TryGetStyle(formatId, out _))
            throw new FormatUnavailableException(formatId);

        var variant = modeResolver.Resolve(folder);
        if (variant == ProjectTypeVariant.ConfigManifest)
        {
            var loader = strategyFactory.GetManifestLoadStrategy();
            if (loader != null)
            {
                var items = loader.Load(folder).ToList();
                if (items.Count > 0) return items;
            }
        }

        // Use the detected format's load strategy (not just Json fallback)
        if (strategy != null) return strategy.Load(folder).ToList();

        // Final fallback: try Json
        return (strategyFactory.GetLoadStrategy(FormatIds.Json)?.Load(folder) ?? []).ToList();
    }

    public void Save(string path, string formatId, List<NsTreeItem> items, IEnumerable<TranslationItem> translations)
    {
        var strategy = strategyFactory.GetSaveStrategy(formatId)
            ?? throw new FormatUnavailableException(formatId);

        var context = new SaveContext
        {
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
        if (settings.LanguageFilePaths?.TryGetValue(language, out var custom) == true)
            return [Path.IsPathRooted(custom) ? custom : Path.Combine(settings.ProjectPath, custom)];

        var strategy = strategyFactory.GetSaveStrategy(settings.SaveFormat);
        return strategy?.LanguageFiles(settings.ProjectPath, language)
            ?? [Path.Combine(settings.ProjectPath, language + ".json")];
    }

    private string ResolveDefaultPath(string formatId, string language) =>
        strategyFactory.GetSaveStrategy(formatId)?.DefaultFilePath(language) ?? $"{language}.json";
}
