using System.IO;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Manages translation packages within a project.
/// Each package maps to a set of per-language files defined in translationUrls.
/// </summary>
public class PackageService(IFileService fileService, IEnumerable<ILoadStrategy> loadStrategies, IEnumerable<ISaveStrategy> saveStrategies, IEnumerable<IProjectCompatibilityImporter>? compatibilityImporters = null) : IPackageService
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
            var loaded = Factory.ReadResource(new(settings.ProjectPath, resource, ProjectService.EffectiveOptions(settings)));
            if (loaded.Document == null || loaded.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error))
                throw new FormatException(string.Join("; ", loaded.Diagnostics.Select(d => $"{resource.RelativePath}: {d.Message}")));
            items.AddRange(loaded.Entries);
            documents.Add(loaded.Document);
        }
        foreach (var importer in compatibilityImporters ?? []) importer.ApplyMetadata(settings.Compatibility, items);
        foreach (var item in items) item.Language = settings.LanguageAliases?.GetValueOrDefault(item.Language) ?? item.Language;
        ProjectService.RestoreStates(settings, items);
        ResourceJsonCodec.ResolveSelectors(items);
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
        if (list.Any(i => !resources.Any(r => (settings.LanguageAliases?.GetValueOrDefault(r.Language) ?? r.Language) == i.Language) || i.Identity is { } id && !resources.Any(r => r.Id == id.ResourceId)))
            throw new InvalidOperationException("An entry belongs to a different package or language.");
        var documents = resources.Select(r => settings.ResourceDocuments.FirstOrDefault(d => d.Resource.Id == r.Id)
            ?? Factory.ReadResource(new(settings.ProjectPath, r, ProjectService.EffectiveOptions(settings))).Document
            ?? throw new FormatException($"{r.RelativePath}: resource could not be loaded.")).ToList();
        var transaction = SaveTransaction.BeginIfNeeded(settings.ProjectPath, resources.Select(r => ResourceFilePath.Resolve(settings.ProjectPath, r.RelativePath)).Append(Path.Combine(settings.ProjectPath, "toucan.tproj")));
        var priorStates = settings.TranslationStates;
        var descriptors = documents.Select(d => d.Resource).ToList();
        var detected = documents.Select(d => d.DetectedOptions).ToList();
        var snapshots = documents.Select(d => new Dictionary<string, System.Text.Json.JsonElement>(d.Data)).ToList();
        try
        {
            foreach (var document in documents)
            {
                var originals = list.Where(i => i.Language == (settings.LanguageAliases?.GetValueOrDefault(document.Resource.Language) ?? document.Resource.Language)).ToList();
                var entries = originals.Select(i => { var copy = ResourceEntryOperations.Copy(i); copy.Language = document.Resource.Language; return copy; }).ToList();
                foreach (var item in entries.Where(i => i.Identity is null))
                {
                    var key = item.Namespace.StartsWith(document.Resource.SelectorPrefix, StringComparison.Ordinal) ? item.Namespace[document.Resource.SelectorPrefix.Length..] : item.Namespace;
                    item.Identity = new ResourceEntryIdentity { ResourceId = document.Resource.Id, Package = document.Resource.Package,
                        PackagePrefix = document.Resource.SelectorPrefix, Selector = item.Namespace,
                        Path = ResourceText.NewPath(key, ResourceOptions.Resolve(document.Resource.Options, ProjectService.EffectiveOptions(settings), null, null), document.Resource.FormatId is FormatIds.Arb or FormatIds.JavaProperties or FormatIds.Resx) };
                }
                for (var index = 0; index < entries.Count; index++) originals[index].Identity = entries[index].Identity;
                var result = Factory.WriteResource(new(settings.ProjectPath, document, entries, ResourceOptions.Resolve(ProjectService.EffectiveOptions(settings), new ResourceOptions { SaveEmptyTranslations = true }, null, null)));
                if (result.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error)) throw new FormatException(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
                for (var index = 0; index < entries.Count; index++) originals[index].FormatData = entries[index].FormatData is null ? null : new(entries[index].FormatData!);
            }
            settings.ResourceDocuments.RemoveAll(d => d.Resource.Package == package.Name);
            settings.ResourceDocuments.AddRange(documents);
            var ids = resources.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            settings.TranslationStates = priorStates.Where(s => s.Identity == null || !ids.Contains(s.Identity.ResourceId)).Concat(list.Select(i => new TranslationState
            {
                Identity = i.Identity, Language = i.Language, Key = i.Namespace, Comment = i.Comment, Approved = i.IsApproved,
                Data = i.FormatData?.Where(p => p.Key != NestedJsonParser.JsonScalarKey).ToDictionary(p => p.Key, p => p.Value) ?? []
            })).ToList();
            settings.Save();
            transaction?.Commit();
        }
        catch
        {
            settings.TranslationStates = priorStates;
            for (var i = 0; i < documents.Count; i++)
            { documents[i].Data = snapshots[i]; documents[i].Resource = descriptors[i]; documents[i].DetectedOptions = detected[i]; }
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
                var result = Factory.WriteResource(new(settings.ProjectPath, document, [], ProjectService.EffectiveOptions(settings)));
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
