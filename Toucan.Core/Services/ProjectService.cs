using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Toucan.Core.Models;
using System.Text;
using Toucan.Extensions;
using System.IO;
using Toucan.Core.Contracts.Services;
using System.Linq;
using System.Collections.Generic;

namespace Toucan.Core.Services;

public class ProjectService : IProjectService
{

    private readonly IFileService _fileService;
    private readonly IEnumerable<ISaveStrategy> _saveStrategies;

    public ProjectService(IFileService fileService, IEnumerable<ISaveStrategy> saveStrategies)
    {
        _fileService = fileService;
        _saveStrategies = saveStrategies;
    }

    public void CreateLanguage(string folder, string language)
    {
        // Persist a minimal JSON object for language files
        _fileService.Save(folder, language + ".json", new Dictionary<string, string>() { { "app", "" } });
    }

    public List<TranslationItem> Load(string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return new List<TranslationItem>();
        }

        string[] files = Directory.GetFiles(folder, "*.json");
        List<TranslationItem> settings = new List<TranslationItem>();

        foreach (string filePath in files)
        {
            List<TranslationItem> newFiles = new List<TranslationItem>();
            string file = Path.GetFileName(filePath);
            string language = Path.GetFileNameWithoutExtension(filePath);

            // Use file service to deserialize JSON content; we read it as dynamic/JObject
            var myObj = _fileService.Read<dynamic>(folder, file);
            FromNestMethod(newFiles, language, myObj);
            if (newFiles.Count == 0)
                newFiles.AddRange(new List<TranslationItem>() { new TranslationItem() { Language = language } });
            settings.AddRange(newFiles);
        }
        //GenerateLargeTestData(translationItem, translationItem.ToLanguages().ToList());
        return settings;
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
