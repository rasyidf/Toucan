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

    private ITranslationStrategyFactory Factory => new TranslationStrategyFactory(saveStrategies, loadStrategies);

    private static List<ResourceDescriptor> Resources(ProjectSettings settings, TranslationPackage package) =>
        package.TranslationUrls.Select(url => settings.ResourceDocuments.FirstOrDefault(d => d.Resource.Package == package.Name && d.Resource.Language == url.Language)?.Resource
            ?? new ResourceDescriptor { Id = url.Path.Replace('\\', '/'), RelativePath = url.Path, Language = url.Language,
                FormatId = settings.SaveFormat, Package = package.Name, Options = settings.ResourceOptions }).ToList();

    public List<TranslationItem> LoadPackage(ProjectSettings settings, string packageName)
    {
        var package = settings.TranslationPackages.FirstOrDefault(p => string.Equals(p.Name, packageName, StringComparison.OrdinalIgnoreCase));
        if (package == null) return [];
        var items = new List<TranslationItem>();
        var documents = new List<ResourceDocumentState>();
        foreach (var resource in Resources(settings, package))
        {
            var loaded = Factory.ReadResource(new(settings.ProjectPath, resource, settings.ResourceOptions));
            if (loaded.Document == null || loaded.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error))
                throw new FormatException(string.Join("; ", loaded.Diagnostics.Select(d => $"{resource.RelativePath}: {d.Message}")));
            items.AddRange(loaded.Entries);
            documents.Add(loaded.Document);
        }
        settings.ResourceDocuments.RemoveAll(d => d.Resource.Package == package.Name);
        settings.ResourceDocuments.AddRange(documents);
        return items;
    }

    public void SavePackage(ProjectSettings settings, string packageName, IEnumerable<TranslationItem> items)
    {
        var package = settings.TranslationPackages.FirstOrDefault(p => string.Equals(p.Name, packageName, StringComparison.OrdinalIgnoreCase));
        if (package == null) return;
        var resources = Resources(settings, package);
        foreach (var resource in resources)
        {
            ResourceFilePath.Resolve(settings.ProjectPath, resource.RelativePath);
            if (Factory.GetResourceSaveStrategy(resource.FormatId) == null) throw new FormatException($"{resource.RelativePath}: exact writing is unsupported for '{resource.FormatId}'.");
        }
        var list = items.ToList();
        if (list.Any(i => !resources.Any(r => r.Language == i.Language) || i.Identity is { } id && !resources.Any(r => r.Id == id.ResourceId)))
            throw new InvalidOperationException("An entry belongs to a different package or language.");
        var documents = resources.Select(r => settings.ResourceDocuments.FirstOrDefault(d => d.Resource.Id == r.Id)
            ?? Factory.ReadResource(new(settings.ProjectPath, r, settings.ResourceOptions)).Document
            ?? throw new FormatException($"{r.RelativePath}: resource could not be loaded.")).ToList();
        var transaction = SaveTransaction.BeginIfNeeded(settings.ProjectPath, resources.Select(r => ResourceFilePath.Resolve(settings.ProjectPath, r.RelativePath)));
        var snapshots = documents.Select(d => new Dictionary<string, System.Text.Json.JsonElement>(d.Data)).ToList();
        try
        {
            foreach (var document in documents)
            {
                var entries = list.Where(i => i.Language == document.Resource.Language).ToList();
                foreach (var item in entries.Where(i => i.Identity is null))
                {
                    var key = item.Namespace.StartsWith(document.Resource.SelectorPrefix, StringComparison.Ordinal) ? item.Namespace[document.Resource.SelectorPrefix.Length..] : item.Namespace;
                    item.Identity = new ResourceEntryIdentity { ResourceId = document.Resource.Id, Package = document.Resource.Package,
                        PackagePrefix = document.Resource.SelectorPrefix, Selector = item.Namespace,
                        Path = document.Resource.FormatId is FormatIds.Arb or FormatIds.JavaProperties or FormatIds.Resx || ResourceOptions.Resolve(document.Resource.Options, settings.ResourceOptions, null, null).Layout is ResourceLayout.Literal or ResourceLayout.Dotted
                            ? [ResourcePathSegment.Named(key)] : ResourceEntryOperations.ParseSelector(key) };
                }
                var result = Factory.WriteResource(new(settings.ProjectPath, document, entries, settings.ResourceOptions with { SaveEmptyTranslations = settings.SaveEmptyTranslations ?? true }));
                if (result.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error)) throw new FormatException(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            }
            transaction?.Commit();
            settings.ResourceDocuments.RemoveAll(d => d.Resource.Package == package.Name);
            settings.ResourceDocuments.AddRange(documents);
        }
        catch
        {
            for (var i = 0; i < documents.Count; i++) documents[i].Data = snapshots[i];
            transaction?.Rollback();
            throw;
        }
    }

    public TranslationPackage CreatePackage(ProjectSettings settings, string name)
    {
        var pkg = new TranslationPackage { Name = name, TranslationUrls = [] };

        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..") throw new ArgumentException("Package name must be a single path segment.", nameof(name));
        if (settings.TranslationPackages.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Package already exists.", nameof(name));
        var strategy = Factory.GetSaveStrategy(settings.SaveFormat) ?? throw new FormatUnavailableException(settings.SaveFormat);
        if (Factory.GetResourceSaveStrategy(settings.SaveFormat) == null) throw new FormatException($"Exact writing is unsupported for '{settings.SaveFormat}'.");
        var documents = settings.Languages.Select(lang => new ResourceDocumentState { Resource = new ResourceDescriptor
        {
            RelativePath = $"locales/{name}/{strategy.DefaultFilePath(lang)}", FormatId = settings.SaveFormat, Package = name, Language = lang, Options = settings.ResourceOptions
        }}).ToList();
        foreach (var document in documents) ResourceFilePath.Resolve(settings.ProjectPath, document.Resource.RelativePath);
        var priorDocuments = settings.ResourceDocuments.ToList();
        // Preserve existing packages when a formerly folder-based project first gains explicit resources.
        foreach (var existing in settings.TranslationPackages)
            if (!settings.ResourceDocuments.Any(d => d.Resource.Package == existing.Name)) LoadPackage(settings, existing.Name);
        var transaction = SaveTransaction.BeginIfNeeded(settings.ProjectPath, documents.Select(d => ResourceFilePath.Resolve(settings.ProjectPath, d.Resource.RelativePath)).Append(Path.Combine(settings.ProjectPath, "toucan.tproj")));
        try
        {
            foreach (var document in documents)
            {
                var result = Factory.WriteResource(new(settings.ProjectPath, document, [], settings.ResourceOptions));
                if (result.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error)) throw new FormatException(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                pkg.TranslationUrls.Add(new() { Language = document.Resource.Language, Path = document.Resource.RelativePath });
            }
            settings.ResourceDocuments.AddRange(documents);
            settings.TranslationPackages.Add(pkg);
            settings.Save();
            transaction?.Commit();
        }
        catch
        {
            settings.ResourceDocuments = priorDocuments;
            settings.TranslationPackages.Remove(pkg);
            transaction?.Rollback();
            throw;
        }
        return pkg;
    }

    public void RemovePackage(ProjectSettings settings, string packageName)
    {
        var pkg = settings.TranslationPackages.FirstOrDefault(p =>
            string.Equals(p.Name, packageName, StringComparison.OrdinalIgnoreCase));
        if (pkg == null) return;

        var files = pkg.TranslationUrls.Select(url => ResourceFilePath.Resolve(settings.ProjectPath, url.Path)).ToList();
        var priorDocuments = settings.ResourceDocuments.ToList();
        var priorPackages = settings.TranslationPackages.ToList();
        var transaction = SaveTransaction.BeginIfNeeded(settings.ProjectPath, files.Append(Path.Combine(settings.ProjectPath, "toucan.tproj")));
        try
        {
            foreach (var file in files) if (File.Exists(file)) File.Delete(file);
            settings.ResourceDocuments.RemoveAll(d => d.Resource.Package == pkg.Name);
            settings.TranslationPackages.Remove(pkg);
            settings.Save();
            transaction?.Commit();
        }
        catch
        {
            settings.ResourceDocuments = priorDocuments;
            settings.TranslationPackages = priorPackages;
            transaction?.Rollback();
            throw;
        }
    }

    public void RenamePackage(ProjectSettings settings, string oldName, string newName)
    {
        var pkg = settings.TranslationPackages.FirstOrDefault(p =>
            string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));
        if (pkg == null) return;

        if (string.IsNullOrWhiteSpace(newName) || settings.TranslationPackages.Any(p => p != pkg && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Package name must be nonempty and unique.", nameof(newName));
        pkg.Name = newName;
        // A package label is independent of its resource destination; renaming must not strand files.
        foreach (var document in settings.ResourceDocuments.Where(d => d.Resource.Package == oldName))
            document.Resource = document.Resource with { Package = newName };

        settings.Save();
    }
}
