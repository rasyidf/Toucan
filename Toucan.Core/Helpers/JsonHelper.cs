using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using System.Text;
using System.IO;

namespace Toucan.Core.Extensions; 

public class JsonParser : IParser
{
    private string Language;
    public JsonParser(string language)
    {
        Language = language;
    }
    public IParser SetLanguage(string language)
    {
        Language = language;
        return this;
    }

    public async IAsyncEnumerable<TranslationItem> Parse(string content)
    {

        dynamic myObj = JsonConvert.DeserializeObject(content) ?? new object();
        var walkItems = new Queue<JToken>();

        foreach (dynamic item in myObj.Children())
        {
            walkItems.Enqueue(item);
        }

        while (walkItems.TryDequeue(out var item))
        {
            if (item.Children().Any())
            {
                foreach (JToken i in item.Children())
                {
                    walkItems.Enqueue(i);
                }
            }
            else
            {
                if (item.Type == JTokenType.Object && !item.Any())
                {
                    Console.WriteLine($"Skipped: {item.Path}");
                }
                else
                {
                    yield return new TranslationItem
                    {
                        Namespace = CleanPath(item.Path),
                        Value = item.Value<string>() ?? "",
                        Language = Language
                    };
                }
            }
        }

    }

    private static string CleanPath(string path)
    {
        string newPath = path;
        if (newPath.StartsWith("['", StringComparison.InvariantCulture))
        {
            newPath = newPath[2..];
        }
        if (newPath.EndsWith("']", StringComparison.InvariantCulture))
        {
            newPath = newPath[..^2];
        }

        return newPath;
    }

    public static void SaveNs(string path, List<NsTreeItem> items, List<string> languages)
    {

        foreach (string language in languages)
        {
            Dictionary<string, dynamic> dyn = [];

            for (int i = 0; i < items?.Count; i++)
            {
                NsTreeItem v = items[i];
                v.ToJson(dyn, language);
            }

            string newFilePath = Path.Combine(path, language + ".json");
            string json = JsonConvert.SerializeObject(dyn, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            });
            File.WriteAllText(newFilePath, json);
        }

    }


    public static void Save(string path, Dictionary<string, IEnumerable<TranslationItem>> TranslationItems)
    {

        foreach (KeyValuePair<string, IEnumerable<TranslationItem>> TranslationItem in TranslationItems)
        {
            string newFilePath = Path.Combine(path, TranslationItem.Key + ".json");
            StringBuilder contentBuilder = new("{\n");
            int counter = 0;
            foreach (TranslationItem setting in TranslationItem.Value.Where(x => !string.IsNullOrEmpty(x.Value)).OrderBy(o => o.Namespace))
            {
                counter++;
                contentBuilder.AppendLine((counter == 1 ? "" : ",") + "\t\"" + setting.Namespace + "\" : \"" + setting.Value + "\"");
            }

            contentBuilder.AppendLine("}");
            File.WriteAllText(newFilePath, contentBuilder.ToString());
        }
    }
}

