using System.IO;
using System.Text;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services.SaveStrategies;

public class IosStringsSaveStrategy(IFileService fileService) : ISaveStrategy
{
    public string FormatId => FormatIds.IosStrings;

    public FormatSupport Support { get; } = new(
        FormatEditing.Limited,
        "Apple .strings (\"key\" = \"value\";)",
        ["Escapes", "unicode", "multiline text", "per-language .lproj folders"],
        [".stringsdict plural files are not read", "Comments in the file are not kept"]);
    public string DisplayName => "iOS .strings";
    public IReadOnlyList<string> FileExtensions => [".strings"];
    public string DefaultFilePath(string language) => $"{language}.lproj/Localizable.strings";

    public FormatDetection Detection { get; } = new(4, [".strings"], []);

    public void Save(string path, SaveContext context)
    {
        if (context?.LanguageDictionary == null) return;

        foreach (var (language, list) in context.LanguageDictionary)
        {
            var dir = Path.Combine(path, $"{language}.lproj");
            Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine($"/* {language} */");
            foreach (var item in list.NoEmpty().OrderBy(i => i.Namespace))
                sb.AppendLine($"\"{Escape(item.Namespace)}\" = \"{Escape(item.Value ?? "")}\";");

            File.WriteAllText(Path.Combine(dir, "Localizable.strings"), sb.ToString(), Encoding.UTF8);
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    public Task SaveAsync(string path, SaveContext context) => Task.Run(() => Save(path, context));
}
