using System.IO;
using System.Text.Json;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class ArbSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public SaveStyles Style => SaveStyles.Arb;

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var dict = new Dictionary<string, object> { ["@@locale"] = language };
            foreach (var item in list.NoEmpty().OrderBy(i => i.Namespace))
                dict[item.Namespace] = item.Value ?? "";

            var json = JsonSerializer.Serialize(dict, s_options);
            File.WriteAllText(Path.Combine(path, $"app_{language}.arb"), json);
        }
    }

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
