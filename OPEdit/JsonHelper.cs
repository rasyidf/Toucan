using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OPEdit.Core.Models;
using OPEdit.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OPEdit.Core.Services;

public class ProjectHelper
{

    public static void CreateLanguage(string folder, string language)
    {
        File.WriteAllText($"{folder}/{language}.json", /*lang=json,strict*/ "{ \"app\": \"\" }");
    }

    public List<LanguageSetting> Load(string folder)
    {
        string[] files = Directory.GetFiles(folder, "*.json");
        List<LanguageSetting> settings = new();

        foreach (string filePath in files)
        {
            List<LanguageSetting> newFiles = new();
            string file = Path.GetFileName(filePath);
            string language = file.Replace(".json", "");

            string content = string.Join(Environment.NewLine, File.ReadAllLines(filePath));
            FromNestMethod(newFiles, language, content);
            if (!newFiles.Any())
                newFiles.AddRange(new LanguageSetting[] { new LanguageSetting() { Language = language } });
            settings.AddRange(newFiles);
        }
        //GenerateLargeTestData(settings, settings.ToLanguages().ToList());
        return settings;
    }

    private void FromNestMethod(List<LanguageSetting> settings, string language, string content)
    {
        List<LanguageSetting> languageSettings = new();
        try
        {
            dynamic myObj = JsonConvert.DeserializeObject(content) ?? new object();
            foreach (JProperty jproperty in myObj)
            {
                ProcessSettings(language, languageSettings, jproperty);
            }
        }
        catch (Exception ex)
        {
            throw ex;
        }
        settings.AddRange(languageSettings);
    }

    private void ProcessSettings(string language, List<LanguageSetting> list, JToken property)
    {
        if (property.Children().Any())
        {
            foreach (JToken childProperty in property.Children())
            {
                ProcessSettings(language, list, childProperty);
            }
        }
        else
        {
            list.Add(new LanguageSetting() { Namespace = CleanPath(property.Path), Value = property.ToObject<string>(), Language = language });
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


    public static void SaveNsJson(string path, List<NsTreeItem> items, List<string> languages)
    {

        foreach (string language in languages)
        {
            Dictionary<string, dynamic> dyn = new();

            for (int i = 0; i < items.Count; i++)
            {
                items[i].ToJson(dyn, language);
            }

            //cleanup empty


            string newFilePath = Path.Combine(path, language + ".json");
            string json = JsonConvert.SerializeObject(dyn, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            });
            File.WriteAllText(newFilePath, json);
        }

    }


    public static void SaveJson(string path, Dictionary<string, IEnumerable<LanguageSetting>> languageSettings)
    {

        foreach (KeyValuePair<string, IEnumerable<LanguageSetting>> languageSetting in languageSettings)
        {
            string newFilePath = Path.Combine(path, languageSetting.Key + ".json");
            StringBuilder contentBuilder = new("{\n");
            int counter = 0;
            foreach (LanguageSetting setting in languageSetting.Value.NoEmpty().OrderBy(o => o.Namespace))
            {
                counter++;
                contentBuilder.AppendLine((counter == 1 ? "" : ",") + "\t\"" + setting.Namespace + "\" : \"" + setting.Value + "\"");
            }

            contentBuilder.AppendLine("}");
            File.WriteAllText(newFilePath, contentBuilder.ToString());
        }
    }
}


