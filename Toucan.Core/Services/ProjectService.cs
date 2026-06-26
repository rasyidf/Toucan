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

    public ProjectLoadResult LoadProject(string folder)
    {
        // Try loading project settings from toucan.project manifest
        var settings = ProjectSettings.LoadFrom(folder);
        if (settings == null)
        {
            settings = ProjectSettings.CreateDefault(folder);
            settings.SaveStyle = FrameworkDetector.Detect(folder);
        }
        settings.ProjectPath = folder;

        // Load translations
        var translations = Load(folder);

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

    public ProjectSettings CreateProject(string folder, IEnumerable<string> languages, SaveStyles style = SaveStyles.Json, bool createManifest = true, string? name = null)
    {
        Directory.CreateDirectory(folder);

        var langList = languages.ToList();
        foreach (var language in langList)
            CreateLanguage(folder, language, style);

        var settings = new ProjectSettings
        {
            Name = name ?? Path.GetFileName(folder),
            ProjectPath = folder,
            PrimaryLanguage = langList.FirstOrDefault() ?? "en-US",
            Languages = langList,
            SaveStyle = style
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
            var reverse = project.LanguageAliases.ToDictionary(kv => kv.Value, kv => kv.Key);
            var list = translations.ToList();
            foreach (var t in list)
                if (reverse.TryGetValue(t.Language, out var fileCode))
                    t.Language = fileCode;
            toSave = list;
        }

        Save(project.ProjectPath, project.SaveStyle, items, toSave);

        // Restore display codes for in-memory state
        if (project.LanguageAliases is { Count: > 0 })
            foreach (var t in (IEnumerable<TranslationItem>)toSave)
                if (project.LanguageAliases.TryGetValue(t.Language, out var mapped))
                    t.Language = mapped;

        // Update project manifest with current language list
        project.Languages = translations.ToLanguages().ToList();
        project.Save();
    }

    public void CreateLanguage(string folder, string language, SaveStyles style = SaveStyles.Json)
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

        var strategy = saveStrategies.FirstOrDefault(s => s.Style == style);
        if (strategy != null)
            strategy.Save(folder, context);
        else
            fileService.Save(folder, language + ".json", new Dictionary<string, string> { { "app", "" } });
    }

    public List<TranslationItem> Load(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return [];

        var variant = modeResolver.Resolve(folder);
        if (variant == ProjectTypeVariant.ConfigManifest)
        {
            var loader = strategyFactory.GetManifestLoadStrategy();
            if (loader != null) return loader.Load(folder).ToList();
        }

        return (strategyFactory.GetLoadStrategy(SaveStyles.Json)?.Load(folder) ?? []).ToList();
    }

    public void Save(string path, SaveStyles style, List<NsTreeItem> items, IEnumerable<TranslationItem> translations)
    {
        var strategy = saveStrategies.FirstOrDefault(s => s.Style == style)
            ?? throw new NotSupportedException($"No save strategy registered for {style}");

        var context = new SaveContext
        {
            LanguageDictionary = translations.ToLanguageDictionary(),
            NsTreeItems = items ?? [],
            Languages = translations.ToLanguages().ToList()
        };

        strategy.Save(path, context);
    }
}
