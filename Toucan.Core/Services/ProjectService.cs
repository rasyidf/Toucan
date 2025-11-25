using Newtonsoft.Json.Linq;
using Toucan.Core.Models;
using Toucan.Extensions;
using System.IO;
using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Services;

public class ProjectService : IProjectService
{

    private readonly IFileService _fileService;
    private readonly IEnumerable<ISaveStrategy> _saveStrategies;
    private readonly ITranslationStrategyFactory _strategyFactory;
    private readonly IProjectModeResolver _modeResolver;
    private readonly Microsoft.Extensions.Logging.ILogger<ProjectService> _logger;

    public ProjectService(IFileService fileService, IEnumerable<ISaveStrategy> saveStrategies, ITranslationStrategyFactory strategyFactory, IProjectModeResolver modeResolver, Microsoft.Extensions.Logging.ILogger<ProjectService> logger)
    {
        _fileService = fileService;
        _saveStrategies = saveStrategies;
        _strategyFactory = strategyFactory;
        _modeResolver = modeResolver;
        _logger = logger;
    }

    // Backward-compatible constructor for existing non-DI callers
    public ProjectService(IFileService fileService, IEnumerable<ISaveStrategy> saveStrategies)
        : this(fileService, saveStrategies, new TranslationStrategyFactory(saveStrategies, new List<ILoadStrategy>()), new ProjectModeResolver(), Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectService>.Instance)
    {
    }

    public void CreateLanguage(string folder, string language, SaveStyles style = SaveStyles.Json)
    {
        // Create a minimal translation structure
        var translations = new List<TranslationItem>
        {
            new TranslationItem { Namespace = "app", Value = string.Empty, Language = language }
        };

        var context = new SaveContext
        {
            LanguageDictionary = new Dictionary<string, IEnumerable<TranslationItem>> { { language, translations } },
            NsTreeItems = new List<NsTreeItem>(),
            Languages = new List<string> { language }
        };

        var strategy = _saveStrategies?.FirstOrDefault(s => s.Style == style);
        if (strategy != null)
        {
            strategy.Save(folder, context);
        }
        else
        {
            // Fallback to JSON if no strategy found
            _fileService.Save(folder, language + ".json", new Dictionary<string, string>() { { "app", "" } });
        }
    }

    public void CreateProject(string folder, IEnumerable<string> languages, SaveStyles style = SaveStyles.Json)
    {
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        foreach (var language in languages)
        {
            CreateLanguage(folder, language, style);
        }
    }

    public List<TranslationItem> Load(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return new List<TranslationItem>();

        // Decide which loading mode to use (config vs folder scan)
        var variant = _modeResolver?.Resolve(folder) ?? ProjectTypeVariant.FolderScan;
        if (variant == ProjectTypeVariant.ConfigManifest)
        {
            var loader = _strategyFactory?.GetManifestLoadStrategy();
            if (loader != null)
                return loader.Load(folder).ToList();
            // fallback
            return LoadByStyle(folder, SaveStyles.Json).ToList();
        }

        // Folder scan - choose JSON loader for now
        return LoadByStyle(folder, SaveStyles.Json).ToList();
    }

    // New helper to load via registered load strategies
    private IEnumerable<TranslationItem> LoadByStyle(string folder, SaveStyles style)
    {
        var loader = _strategyFactory?.GetLoadStrategy(style);
        if (loader != null)
            return loader.Load(folder);

        return new List<TranslationItem>();
    }

    private static void FromNestMethod(List<TranslationItem> translationItem, string language, dynamic content)
    {
        List<TranslationItem> TranslationItems = new List<TranslationItem>();
        try
        {
            if (content == null) return;

            foreach (JProperty jproperty in content)
            {
                ProcessLanguage(language, TranslationItems, jproperty);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error :" + ex.Message);
        }
        translationItem.AddRange(TranslationItems);
    }

    private static void ProcessLanguage(string language, List<TranslationItem> list, JToken property)
    {
        if (property.Children().Any())
        {
            foreach (JToken childProperty in property.Children())
            {
                ProcessLanguage(language, list, childProperty);
            }
        }
        else
        {
            list.Add(new TranslationItem() { Namespace = CleanPath(property.Path), Value = property.ToObject<string>(), Language = language });
        }
    }

    private static string CleanPath(string path)
    {
        string newPath = path;
        if (newPath.StartsWith("['"))
        {
            newPath = newPath.Substring(2);
        }
        if (newPath.EndsWith("']"))
        {
            newPath = newPath.Substring(0, newPath.Length - 2);
        }

        return newPath;
    }


    public void SaveNsJson(string path, List<NsTreeItem> items, List<string> languages)
    {

        foreach (string language in languages)
        {
            Dictionary<string, dynamic> dyn = new Dictionary<string, dynamic>();

            for (int i = 0; i < items.Count; i++)
            {
                items[i].ToJson(dyn, language);
            }

            //cleanup empty


            _fileService.Save(path, language + ".json", dyn);
        }

    }


    public void SaveJson(string path, Dictionary<string, IEnumerable<TranslationItem>> TranslationItems)
    {

        foreach (KeyValuePair<string, IEnumerable<TranslationItem>> TranslationItem in TranslationItems)
        {
            var dict = new Dictionary<string, string>();
            foreach (TranslationItem setting in TranslationItem.Value.NoEmpty().OrderBy(o => o.Namespace))
            {
                dict[setting.Namespace] = setting.Value ?? string.Empty;
            }

            _fileService.Save(path, TranslationItem.Key + ".json", dict);
        }
    }

    // Pluggable save entrypoint — selects a registered save strategy by SaveStyles
    public void Save(string path, SaveStyles style, List<NsTreeItem> items, IEnumerable<TranslationItem> translations)
    {
        var strategy = _saveStrategies?.FirstOrDefault(s => s.Style == style);
        if (strategy == null)
            throw new NotSupportedException($"No save strategy registered for {style}");

        var context = new SaveContext()
        {
            LanguageDictionary = translations?.ToLanguageDictionary() ?? new Dictionary<string, IEnumerable<TranslationItem>>(),
            NsTreeItems = items ?? new List<NsTreeItem>(),
            Languages = translations?.ToLanguages().ToList() ?? new List<string>()
        };

        strategy.Save(path, context);
    }
}
