using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Manages translation packages within a project.
/// Each package maps to a set of per-language files defined in translationUrls.
/// </summary>
public class PackageService(IFileService fileService, IEnumerable<ILoadStrategy> loadStrategies, IEnumerable<ISaveStrategy> saveStrategies) : IPackageService
{
    public IEnumerable<TranslationPackage> GetPackages(ProjectSettings settings)
        => settings.TranslationPackages.Count > 0
            ? settings.TranslationPackages
            : [new TranslationPackage { Name = "main" }];

    public List<TranslationItem> LoadPackage(ProjectSettings settings, string packageName)
    {
        var pkg = settings.TranslationPackages.FirstOrDefault(p =>
            string.Equals(p.Name, packageName, StringComparison.OrdinalIgnoreCase));
        if (pkg == null || pkg.TranslationUrls.Count == 0) return [];

        var items = new List<TranslationItem>();
        var loader = loadStrategies.FirstOrDefault(l => l.Style == settings.SaveStyle)
            ?? loadStrategies.FirstOrDefault(l => l.Style == SaveStyles.Json);

        foreach (var url in pkg.TranslationUrls)
        {
            var filePath = Path.Combine(settings.ProjectPath, url.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(filePath)) continue;

            var dir = Path.GetDirectoryName(filePath)!;
            var content = fileService.ReadText(dir, Path.GetFileName(filePath));
            if (string.IsNullOrWhiteSpace(content)) continue;

            // Use the nested JSON parser for package files
            var parsed = NestedJsonParser.ParseNestedJson(url.Language, content);
            items.AddRange(parsed);
        }
        return items;
    }

    public void SavePackage(ProjectSettings settings, string packageName, IEnumerable<TranslationItem> items)
    {
        var pkg = settings.TranslationPackages.FirstOrDefault(p =>
            string.Equals(p.Name, packageName, StringComparison.OrdinalIgnoreCase));
        if (pkg == null) return;

        var byLanguage = items.GroupBy(i => i.Language);
        foreach (var langGroup in byLanguage)
        {
            var url = pkg.TranslationUrls.FirstOrDefault(u => u.Language == langGroup.Key);
            if (url == null) continue;

            var filePath = Path.Combine(settings.ProjectPath, url.Path.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(dir);

            var context = new SaveContext
            {
                LanguageDictionary = new Dictionary<string, IEnumerable<TranslationItem>> { { langGroup.Key, langGroup } },
                NsTreeItems = [],
                Languages = [langGroup.Key]
            };

            var strategy = saveStrategies.FirstOrDefault(s => s.Style == settings.SaveStyle)
                ?? saveStrategies.FirstOrDefault(s => s.Style == SaveStyles.Json);
            strategy?.Save(dir, context);
        }
    }

    public TranslationPackage CreatePackage(ProjectSettings settings, string name)
    {
        var pkg = new TranslationPackage { Name = name, TranslationUrls = [] };

        // Generate URLs for each language
        foreach (var lang in settings.Languages)
        {
            var path = $"locales/{lang}/{name}.json";
            pkg.TranslationUrls.Add(new TranslationUrl { Language = lang, Path = path });

            // Create empty file
            var fullPath = Path.Combine(settings.ProjectPath, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            if (!File.Exists(fullPath))
                File.WriteAllText(fullPath, "{}");
        }

        settings.TranslationPackages.Add(pkg);
        settings.Save();
        return pkg;
    }

    public void RemovePackage(ProjectSettings settings, string packageName)
    {
        var pkg = settings.TranslationPackages.FirstOrDefault(p =>
            string.Equals(p.Name, packageName, StringComparison.OrdinalIgnoreCase));
        if (pkg == null) return;

        // Delete associated files
        foreach (var url in pkg.TranslationUrls)
        {
            var filePath = Path.Combine(settings.ProjectPath, url.Path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(filePath)) File.Delete(filePath);
        }

        settings.TranslationPackages.Remove(pkg);
        settings.Save();
    }

    public void RenamePackage(ProjectSettings settings, string oldName, string newName)
    {
        var pkg = settings.TranslationPackages.FirstOrDefault(p =>
            string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (pkg == null) return;

        pkg.Name = newName;
        // Update file paths
        foreach (var url in pkg.TranslationUrls)
            url.Path = url.Path.Replace($"/{oldName}.", $"/{newName}.");

        settings.Save();
    }
}
