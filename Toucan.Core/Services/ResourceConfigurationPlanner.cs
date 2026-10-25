using System.Security.Cryptography;
using System.Text.Json;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

public sealed record ResourceConfigurationPlan(ProjectSettings Settings, IReadOnlyList<TranslationItem> Entries,
    IReadOnlyList<ResourceDiagnostic> Diagnostics, IReadOnlyDictionary<string, string> DestinationHashes,
    IReadOnlyList<string> Preview)
{
    public IReadOnlyDictionary<string, string> ExternalHashes { get; init; } = new Dictionary<string, string>();
    public bool CanApply => Diagnostics.All(d => d.Severity != ResourceDiagnosticSeverity.Error);
}

/// <summary>Plans resource edits against copies. Serializers only see a private temporary project.</summary>
public sealed class ResourceConfigurationPlanner(ITranslationStrategyFactory factory)
{
    public static string Fingerprint(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "";

    public ResourceConfigurationPlan Plan(ProjectSettings current, ProjectSettings proposed, IReadOnlyList<TranslationItem> entries,
        IReadOnlySet<string>? reloadInputs = null, bool workspaceHasEdits = false,
        IReadOnlyDictionary<string, string>? conversions = null, IReadOnlyDictionary<string, string>? externalCopies = null, CancellationToken cancellationToken = default)
    {
        var next = ProjectConfiguration.Copy(proposed);
        var diagnostics = new List<ResourceDiagnostic>();
        var hashes = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var externalHashes = new Dictionary<string, string>();
        var result = new List<TranslationItem>();
        var preview = new List<string>();
        foreach (var original in current.ResourceDocuments)
        {
            var originalFile = ResourceFilePath.Resolve(current.ProjectPath, original.Resource.RelativePath);
            hashes[originalFile] = Fingerprint(originalFile);
        }
        var destinations = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var old = current.ResourceDocuments.ToDictionary(d => d.Resource.Id);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var temp = Path.Combine(Path.GetTempPath(), "Toucan-resource-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (var document in next.ResourceDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resource = document.Resource;
                if (resource.Dialect == "formatjs-target" && !old.ContainsKey(resource.Id))
                { resource = resource with { Options = resource.Options with { Layout = ResourceLayout.Literal } }; document.Resource = resource; }
                try
                {
                    if (!ids.Add(resource.Id) || string.IsNullOrWhiteSpace(resource.Id) || string.IsNullOrWhiteSpace(resource.Language) || string.IsNullOrWhiteSpace(resource.Package))
                        throw new FormatException("Resource IDs must be unique; ID, package and locale are required.");
                    var destination = ResourceFilePath.Resolve(current.ProjectPath, resource.RelativePath);
                    if (!destinations.Add(destination)) throw new FormatException("Multiple resources have the same destination.");
                    hashes[destination] = Fingerprint(destination);
                    if (factory.GetResourceLoadStrategy(resource.FormatId) == null || factory.GetResourceSaveStrategy(resource.FormatId) == null)
                        throw new FormatException("The selected provider does not support exact resource IO.");
                    ResourceText.Validate(resource.Options, resource.RelativePath);
                    var items = new List<TranslationItem>();
                    if (externalCopies != null && externalCopies.TryGetValue(resource.Id, out var external))
                    {
                        if (File.Exists(destination)) throw new FormatException("Copy requires a new in-project destination.");
                        if (new FileInfo(external).Length > 20 * 1024 * 1024) throw new FormatException("External resource exceeds the 20 MiB copy limit.");
                        externalHashes[external] = Fingerprint(external);
                        var read = factory.ReadResource(new(Path.GetDirectoryName(external)!, resource with { RelativePath = Path.GetFileName(external) }, ProjectService.EffectiveOptions(next), cancellationToken));
                        diagnostics.AddRange(read.Diagnostics);
                        if (read.Document == null) throw new FormatException("External resource could not be read.");
                        document.Data = read.Document.Data; document.DetectedOptions = read.Document.DetectedOptions;
                        foreach (var entry in read.Entries)
                        {
                            if (entry.FormatData != null)
                                foreach (var key in entry.FormatData.Keys.Where(k => k.EndsWith(".file", StringComparison.Ordinal)).ToList()) entry.FormatData[key] = resource.RelativePath;
                            items.Add(entry);
                        }
                        preview.Add($"Copy selected external file to {resource.RelativePath}; external source is retained.");
                    }
                    else if (conversions != null && conversions.TryGetValue(resource.Id, out var sourceId))
                    {
                        if (!old.TryGetValue(sourceId, out var conversionSource)) throw new FormatException("Conversion source no longer exists.");
                        if (conversionSource.Resource.Ownership != ResourceOwnership.User || factory.GetResourceLoadStrategy(conversionSource.Resource.FormatId) is IResourceCapabilities restriction && !restriction.Permissions(conversionSource.Resource).EditKeys)
                            throw new FormatException("Generated catalogs cannot be converted into editable resources.");
                        if (File.Exists(destination)) throw new FormatException("Conversion requires a new destination; existing files are never overwritten.");
                        var structured = factory.GetResourceSaveStrategy(resource.FormatId) is IResourceOptionsProvider schema && schema.SupportedOptions(resource).Contains("Layout");
                        foreach (var entry in entries.Where(i => i.Identity?.ResourceId == sourceId))
                        {
                            var copy = ResourceEntryOperations.Copy(entry); var identity = copy.Identity!;
                            var path = structured ? identity.Path : [ResourcePathSegment.Named(ResourceEntryOperations.Selector(identity.Path))];
                            var selector = resource.SelectorPrefix + ResourceEntryOperations.Selector(path, escaped: true);
                            copy.Identity = identity with { ResourceId = resource.Id, Package = resource.Package, PackagePrefix = resource.SelectorPrefix, Path = path, Selector = selector };
                            copy.Namespace = selector; copy.Language = resource.Language; copy.FormatData = null; items.Add(copy);
                        }
                        if (items.GroupBy(i => i.Identity!.KeyToken).Any(g => g.Count() > 1)) throw new FormatException("Conversion would collapse distinct typed keys. Choose a structured format.");
                        diagnostics.Add(new("resource.conversion", "Conversion copies editable text and native review state. Opaque format-specific content remains in the retained original file.", ResourceDiagnosticSeverity.Warning, resource.RelativePath));
                        next.Compatibility["resource-conversion:" + resource.Id] = new CompatibilityEnvelope { Source = "toucan-conversion", Data = new() { ["originalResource"] = JsonSerializer.SerializeToElement(conversionSource.Resource), ["sha256"] = JsonSerializer.SerializeToElement(Fingerprint(ResourceFilePath.Resolve(current.ProjectPath, conversionSource.Resource.RelativePath))) } };
                    }
                    else if (old.TryGetValue(resource.Id, out var prior))
                    {
                        var protectedSource = prior.Resource.Ownership != ResourceOwnership.User || factory.GetResourceLoadStrategy(prior.Resource.FormatId) is IResourceCapabilities c && !c.Permissions(prior.Resource).EditKeys;
                        if (protectedSource && JsonSerializer.Serialize(resource) != JsonSerializer.Serialize(prior.Resource))
                            throw new FormatException("Generated source mappings and options cannot be changed here. Edit and extract the source instead.");
                        if (resource.Ownership != prior.Resource.Ownership || resource.FormatId != prior.Resource.FormatId || resource.Dialect != prior.Resource.Dialect)
                            throw new FormatException("Changing ownership, format or dialect requires a separate resource conversion.");
                        if (!Path.GetFullPath(destination).Equals(ResourceFilePath.Resolve(current.ProjectPath, prior.Resource.RelativePath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && File.Exists(destination))
                            throw new FormatException("The new destination already exists. Select a new path to avoid overwriting an unrelated file.");
                        var inputBefore = ResourceOptions.Resolve(prior.Resource.Options, ProjectService.EffectiveOptions(current), prior.DetectedOptions, null).InputEncoding;
                        var inputAfter = ResourceOptions.Resolve(resource.Options, ProjectService.EffectiveOptions(next), prior.DetectedOptions, null).InputEncoding;
                        if (inputBefore != inputAfter)
                        {
                            if (workspaceHasEdits || reloadInputs?.Contains(resource.Id) != true)
                                throw new FormatException("Save existing edits first, then choose Reload input to apply a new input encoding.");
                            var inputResource = resource with { RelativePath = prior.Resource.RelativePath };
                            var read = factory.ReadResource(new(current.ProjectPath, inputResource, ProjectService.EffectiveOptions(next), cancellationToken));
                            diagnostics.AddRange(read.Diagnostics);
                            if (read.Document == null) throw new FormatException("Could not reload the selected encoding.");
                            document.Data = read.Document.Data; document.DetectedOptions = read.Document.DetectedOptions;
                            items.AddRange(read.Entries);
                            preview.Add($"Reload {prior.Resource.RelativePath} using {inputAfter ?? "detected encoding"}.");
                        }
                        foreach (var entry in entries.Where(i => i.Identity?.ResourceId == resource.Id && inputBefore == inputAfter))
                        {
                            var copy = ResourceEntryOperations.Copy(entry);
                            if (!protectedSource)
                            {
                                copy.Language = resource.Language;
                                var identity = copy.Identity!;
                                var selector = resource.SelectorPrefix + ResourceEntryOperations.Selector(identity.Path, escaped: true);
                                copy.Identity = identity with { Package = resource.Package, PackagePrefix = resource.SelectorPrefix, Selector = selector };
                                copy.Namespace = selector;
                            }
                            items.Add(copy);
                        }
                        if (prior.Resource.RelativePath != resource.RelativePath)
                            preview.Add($"{prior.Resource.RelativePath} → {resource.RelativePath}: copy on Save; original file retained.");
                    }
                    else if (File.Exists(destination))
                    {
                        var read = factory.ReadResource(new(current.ProjectPath, resource, ProjectService.EffectiveOptions(next), cancellationToken));
                        diagnostics.AddRange(read.Diagnostics);
                        if (read.Document != null) { document.Data = read.Document.Data; document.DetectedOptions = read.Document.DetectedOptions; }
                        items.AddRange(read.Entries);
                    }
                    else
                    {
                        if (resource.Ownership != ResourceOwnership.User) throw new FormatException("A generated source must exist and be imported through its provider.");
                        // Populate targets with existing source IDs, keeping extracted source documents untouched.
                        foreach (var source in entries.Where(i => !i.Permissions.EditKeys && i.Identity?.Package == resource.Package))
                        {
                            var copy = ResourceEntryOperations.Copy(source);
                            copy.Permissions = ResourcePermissions.Editable;
                            copy.Value = ""; copy.Comment = ""; copy.IsApproved = false;
                            ResourceEntryOperations.Move(copy, resource, ResourceEntryOperations.Selector(copy.Identity!.Path));
                            items.Add(copy);
                        }
                        if (resource.Dialect == "formatjs-target") document.Resource = resource with { Options = resource.Options with { Layout = ResourceLayout.Literal } };
                    }
                    if (old.TryGetValue(resource.Id, out var origin) && origin.Resource.RelativePath != resource.RelativePath)
                        foreach (var item in items.Where(i => i.FormatData != null))
                            foreach (var hint in item.FormatData!.Keys.Where(k => k.EndsWith(".file", StringComparison.Ordinal) && item.FormatData[k] == origin.Resource.RelativePath).ToList())
                                item.FormatData[hint] = resource.RelativePath;
                    var staged = ResourceFilePath.Resolve(temp, resource.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                    if (File.Exists(destination)) File.Copy(destination, staged);
                    var permissions = resource.Ownership != ResourceOwnership.User ? ResourcePermissions.Generated :
                        factory.GetResourceLoadStrategy(resource.FormatId) is IResourceCapabilities cap ? cap.Permissions(resource) : ResourcePermissions.Editable;
                    foreach (var item in items) { item.Language = resource.Language; item.Permissions = permissions; }
                    var write = factory.WriteResource(new(temp, document, items, ProjectService.EffectiveOptions(next), cancellationToken));
                    diagnostics.AddRange(write.Diagnostics);
                    preview.Add($"{resource.RelativePath} [{resource.Package}/{resource.Language}] — {items.Count} entries");
                    if (File.Exists(staged))
                    {
                        // Keep previews bounded. The writer received copies, never original document state or paths.
                        using var reader = new StreamReader(staged);
                        var buffer = new char[4096]; var length = reader.ReadBlock(buffer, 0, buffer.Length);
                        preview.Add(new string(buffer, 0, length) + (reader.Peek() >= 0 ? "\n…" : ""));
                    }
                    foreach (var item in items) item.Language = next.LanguageAliases?.GetValueOrDefault(resource.Language) ?? resource.Language;
                    result.AddRange(items);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)
                { diagnostics.Add(new("resource.configuration", ex.Message, ResourceDiagnosticSeverity.Error, resource.RelativePath)); }
            }
            foreach (var prior in old.Values.Where(d => !ids.Contains(d.Resource.Id)))
            {
                if (prior.Resource.Ownership != ResourceOwnership.User || factory.GetResourceLoadStrategy(prior.Resource.FormatId) is IResourceCapabilities cap && !cap.Permissions(prior.Resource).EditKeys)
                    diagnostics.Add(new("resource.protected", "Generated source mappings cannot be detached.", ResourceDiagnosticSeverity.Error, prior.Resource.RelativePath));
                preview.Add($"Detach {prior.Resource.RelativePath}; file retained.");
            }
            if (next.ResourceDocuments.Count == 0 && old.Count > 0) diagnostics.Add(new("resource.empty-layout", "Keep at least one exact resource mapping.", ResourceDiagnosticSeverity.Error));
            if (result.GroupBy(i => (i.Language, i.Namespace)).Any(g => g.Count() > 1))
                diagnostics.Add(new("resource.identity-collision", "Mappings would create ambiguous display identities. Choose distinct package prefixes.", ResourceDiagnosticSeverity.Error));
            var formats = next.ResourceDocuments.Select(d => d.Resource.FormatId).Distinct(StringComparer.Ordinal).ToList();
            if (formats.Count == 1) next.SaveFormat = formats[0];
            next.Languages = next.ResourceDocuments.Select(d => next.LanguageAliases?.GetValueOrDefault(d.Resource.Language) ?? d.Resource.Language).Distinct().ToList();
        }
        finally { Directory.Delete(temp, recursive: true); }
        return new(next, result, diagnostics, hashes, preview) { ExternalHashes = externalHashes };
    }
}
