﻿using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Toucan.Core.Models;
using System.Text;
using Toucan.Extensions;
using System.IO;

namespace Toucan.Core;


public static class ProjectHelper
{

    public static void CreateLanguage(string folder, string language)
    {
        File.WriteAllText($"{folder}/{language}.json", /*lang=json,strict*/ "{ \"app\": \"\" }");
    }

    public static List<TranslationItem> Load(string folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return [];
        }

        string[] files = Directory.GetFiles(folder, "*.json");
        List<TranslationItem> settings = [];

        foreach (string filePath in files)
        {
            List<TranslationItem> newFiles = [];
            string file = Path.GetFileName(filePath);
            string language = Path.GetFileNameWithoutExtension(filePath);

            string content = string.Join(Environment.NewLine, File.ReadAllLines(filePath));
            FromNestMethod(newFiles, language, content);
            if (newFiles.Count == 0)
                newFiles.AddRange([new TranslationItem() { Language = language }]);
            settings.AddRange(newFiles);
        }
        //GenerateLargeTestData(translationItem, translationItem.ToLanguages().ToList());
        return settings;
    }

    private static void FromNestMethod(List<TranslationItem> translationItem, string language, string content)
    {
        List<TranslationItem> TranslationItems = [];
        try
        {
            dynamic myObj = JsonConvert.DeserializeObject(content) ?? new object();
            foreach (JProperty jproperty in myObj)
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


    public static void SaveNsJson(string path, List<NsTreeItem> items, List<string> languages)
    {

        foreach (string language in languages)
        {
            Dictionary<string, dynamic> dyn = [];

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


    public static void SaveJson(string path, Dictionary<string, IEnumerable<TranslationItem>> TranslationItems)
    {

        foreach (KeyValuePair<string, IEnumerable<TranslationItem>> TranslationItem in TranslationItems)
        {
            string newFilePath = Path.Combine(path, TranslationItem.Key + ".json");
            StringBuilder contentBuilder = new("{\n");
            int counter = 0;
            foreach (TranslationItem setting in TranslationItem.Value.NoEmpty().OrderBy(o => o.Namespace))
            {
                counter++;
                contentBuilder.AppendLine((counter == 1 ? "" : ",") + "\t\"" + setting.Namespace + "\" : \"" + setting.Value + "\"");
            }

            contentBuilder.AppendLine("}");
            File.WriteAllText(newFilePath, contentBuilder.ToString());
        }
    }
}
