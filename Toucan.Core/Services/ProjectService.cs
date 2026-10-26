using System.IO;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;

namespace Toucan.Core.Services;

public class ProjectService(
    IFileService fileService,
    IEnumerable<ISaveStrategy> saveStrategies,
    ITranslationStrategyFactory strategyFactory,
    IProjectModeResolver modeResolver,
    ILogger<ProjectService> logger,
    IEnumerable<IProjectCompatibilityImporter>? compatibilityImporters = null,
    IEnumerable<Toucan.Core.Contracts.IFrameworkProfile>? frameworkProfiles = null,
    WorkspaceCapabilityResolver? capabilities = null,
    WorkspaceSourceLoader? sourceLoader = null,
    IRecoveryDraftService? recoveryDrafts = null) : IProjectService
{
    public ProjectService(IFileService fileService, IEnumerable<ISaveStrategy> saveStrategies)
        : this(fileService, saveStrategies,
            new TranslationStrategyFactory(saveStrategies, new List<ILoadStrategy>()),
            new ProjectModeResolver(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectService>.Instance)
    { }

    public ProjectLoadResult LoadProject(string folder, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        using var _ = ScanContext.Begin(ct, progress);
        ct.ThrowIfCancellationRequested();
        var selectedFile = File.Exists(folder) ? Path.GetFullPath(folder) : null;
        if (selectedFile != null) folder = Path.GetDirectoryName(selectedFile)!;
        // An explicit file selection must never be replaced by another manifest in the same folder.
        var selectedImporter = selectedFile == null ? null : (compatibilityImporters ?? []).FirstOrDefault(i => i.FindProjects(folder).Any(f => Path.GetFullPath(f).Equals(selectedFile, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)));
        var settings = selectedImporter != null ? FromCompatibility(selectedImporter.Read(selectedFile!)) :
            selectedFile != null ? ProjectSettings.LoadFile(selectedFile) ?? throw new FormatException("The selected native project is invalid.") : ProjectSettings.LoadFrom(folder);
        if (settings == null)
        {
            var projects = (compatibilityImporters ?? []).SelectMany(importer => importer.FindProjects(folder).Select(file => (Importer: importer, File: file))).ToList();
            if (projects.Count > 1) throw new FormatException("This folder has multiple migration projects. Import each project in its own folder.");
            if (projects.Count == 1) settings = FromCompatibility(projects[0].Importer.Read(projects[0].File));
            else
            {
                settings = ProjectSettings.CreateDefault(folder);
                settings.SaveFormat = new FormatDetector(strategyFactory.SaveStrategies).Detect(folder);
                var profile = (frameworkProfiles ?? []).Where(p => p is Toucan.Core.Contracts.IFrameworkFeatures && strategyFactory.GetLoadStrategy(p.DefaultFormatId) is IResourceDiscoveryStrategy).OrderByDescending(p => p.DetectionScore(folder)).FirstOrDefault(p => p.DetectionScore(folder) > 0);
                if (profile is Toucan.Core.Contracts.IFrameworkFeatures features)
                {
                    var resources = profile.DiscoverFiles(folder).Select(features.Describe).ToList();
                    if (resources.Count > 0 && resources.All(r => strategyFactory.GetResourceLoadStrategy(r.FormatId) != null))
                    {
                        settings.Framework = profile.Id; settings.SaveFormat = profile.DefaultFormatId;
                        settings.ResourceDocuments = resources.Select(r => new ResourceDocumentState { Resource = r }).ToList();
                        settings.PrimaryLanguage = resources.First(r => r.Ownership == ResourceOwnership.GeneratedSource || resources.All(d => d.Ownership != ResourceOwnership.GeneratedSource)).Language;
                        settings.Languages = resources.Select(r => r.Language).Distinct().ToList();
                    }
                }
            }
        }
        settings.ProjectPath = folder;
        settings.FrameworkOptions ??= new();
        settings.DefaultPathResolver = ResolveDefaultPath;
        if (settings.ResourceSchemaVersion != 1) throw new FormatException($"Resource schema {settings.ResourceSchemaVersion} is not supported.");

        WorkspaceSource? source = null;
        if (!string.IsNullOrWhiteSpace(settings.Framework))
        {
            var resolved = (capabilities ?? throw new InvalidOperationException("Framework capability resolution is unavailable.")).Resolve(settings.Framework, settings.SaveFormat);
            if (resolved.UsesDocumentPersistence)
            {
                var draft = recoveryDrafts?.TryRead(folder)?.Workspace;
                var fallback = draft is not null && draft.Root == Path.GetFullPath(folder) && draft.FrameworkId == settings.Framework
                    && FormatIds.Comparer.Equals(draft.FormatId, settings.SaveFormat) ? draft.IdentityCheckpoint : null;
                source = (sourceLoader ?? throw new InvalidOperationException("Document loading is unavailable.")).LoadAsync(folder, settings.Framework,
                    settings.SaveFormat, cancellationToken: ct, fallbackIdentityCheckpoint: fallback).GetAwaiter().GetResult();
            }
        }

        // Load translations using the detected/configured format
        var resourceAware = settings.ResourceDocuments.Count > 0 ||
            strategyFactory.GetResourceLoadStrategy(settings.SaveFormat) is IResourceDiscoveryStrategy;
        var translations = source is not null ? source.Units.Select(u => new TranslationItem
        {
            Namespace = string.Join('.', u.Namespace.Segments.Concat(u.KeyPath.Segments)), Language = u.Language, Value = u.Value,
            Comment = u.Metadata?.Comment ?? string.Empty, IsApproved = u.Metadata?.Approved ?? false, LastModifiedUtc = u.Metadata?.Modified,
            ApprovedAtUtc = u.Metadata?.ApprovedAt, ChangeType = u.Metadata?.ChangeType ?? ChangeType.DirectEdit,
            FormatData = u.RawScalar is null ? null : new Dictionary<string, string> { [NestedJsonParser.JsonScalarKey] = u.RawScalar }
        }).ToList() : (resourceAware ? LoadResources(settings, ct) : Load(folder, settings.SaveFormat, true, settings.InputEncoding));

        if (source is not null && settings.LanguageAliases is { Count: > 0 })
            throw new NotSupportedException("Document-aware projects require source language codes; alias migration must be explicit.");

        if (source is not null) source = ImportLegacyAnnotations(source, translations);

        IResourceMetadataAuthority? MetadataAuthority(TranslationItem item)
        {
            var resource = settings.ResourceDocuments.FirstOrDefault(d => d.Resource.Id == item.Identity?.ResourceId)?.Resource;
            return resource == null ? null : strategyFactory.GetResourceLoadStrategy(resource.FormatId) as IResourceMetadataAuthority;
        }
        var inlineComments = settings.TranslationStates.Count > 0
            ? translations.Where(i => MetadataAuthority(i)?.CommentsInResource == true).ToDictionary(i => i, i => i.Comment) : [];
        foreach (var importer in compatibilityImporters ?? []) importer.ApplyMetadata(settings.Compatibility, translations);
        foreach (var comment in inlineComments) comment.Key.Comment = comment.Value;
        // Apply language aliases
        if (settings.LanguageAliases is { Count: > 0 })
            foreach (var t in translations)
                if (settings.LanguageAliases.TryGetValue(t.Language, out var mapped))
                    t.Language = mapped;

        RestoreStates(settings, translations, MetadataAuthority);
        foreach (var item in translations)
        {
            var document = settings.ResourceDocuments.FirstOrDefault(d => d.Resource.Id == item.Identity?.ResourceId);
            if (document != null) item.Permissions = ResourcePermissionsFor(document.Resource);
        }
        if (resourceAware) ResourceJsonCodec.ResolveSelectors(translations);

        // Backfill settings from loaded data if manifest was missing
        if (settings.Languages.Count == 0)
            settings.Languages = translations.ToLanguages().ToList();

        var warnings = settings.ImportDiagnostics.Select(d => $"{d.RelativePath} [{d.Field}]: {d.Message}").Concat(settings.ResourceDocuments.Where(d => d.Data.TryGetValue("yaml.unsafe", out var unsafeInput) && unsafeInput.ValueKind == System.Text.Json.JsonValueKind.String)
            .Select(d => $"{d.Resource.RelativePath}: saving is blocked: {d.Data["yaml.unsafe"].GetString()}")).ToList();
        var strategy = strategyFactory.GetSaveStrategy(settings.SaveFormat);
        var unsupported = (source is null && settings.ResourceDocuments.Count == 0) ? strategy?.FindUnsupportedConstructs(folder) ?? [] : [];
        if (unsupported.Count > 0)
            warnings.Add($"These {strategy!.DisplayName} files contain content Toucan cannot write back: {string.Join("; ", unsupported)}. "
                + "You can edit and review, but saving is blocked to protect your files. Use Save As to write a copy.");

        return new ProjectLoadResult { Settings = settings, Translations = translations, Warnings = warnings, Source = source };
    }

    private WorkspaceSource ImportLegacyAnnotations(WorkspaceSource source, List<TranslationItem> translations)
    {
        if (source.Artifacts.Any(a => a.RelativePath == WorkspaceMetadataCodec.IdentityPath)) return source;
        var comments = new CommentPersistenceService(Microsoft.Extensions.Logging.Abstractions.NullLogger<CommentPersistenceService>.Instance, strategyFactory);
        var legacy = translations.Select(t => new TranslationItem { Namespace = t.Namespace, Language = t.Language, Value = t.Value }).ToList();
        comments.LoadComments(source.Root, source.FormatId, legacy);
        var audit = new AuditService(Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditService>.Instance);
        audit.LoadFromSidecar(source.Root);
        var lookup = legacy.GroupBy(t => (t.Namespace, t.Language)).ToDictionary(g => g.Key, g => g.ToArray());
        var units = source.Units.Select(unit =>
        {
            if (unit.Metadata is not null || source.Artifacts.Any(a => a.RelativePath == WorkspaceMetadataCodec.EntryPath(unit.Binding.DocumentId))) return unit;
            var key = string.Join('.', unit.Namespace.Segments.Concat(unit.KeyPath.Segments));
            var matches = lookup[(key, unit.Language)]; var item = matches[0]; var history = audit.GetMetadata(item);
            if (matches.Length > 1 && (item.Comment.Length > 0 || history is not null))
                throw new InvalidOperationException("Legacy annotations have an ambiguous key; select their source document before migrating.");
            var metadata = new WorkspaceEntryMetadata(unit.EntryId, unit.Language, item.Comment, history?.ApprovedAtUtc is not null,
                history?.LastModifiedUtc, history?.ApprovedAtUtc, history?.ChangeType ?? ChangeType.DirectEdit);
            return unit with { Metadata = metadata.IsEmpty ? null : metadata };
        }).ToArray();
        return new(source.Root, source.FrameworkId, source.FormatId, source.Documents, units, source.IdentityCatalog, source.Artifacts);
    }

    private ResourcePermissions ResourcePermissionsFor(ResourceDescriptor resource) =>
        resource.Ownership != ResourceOwnership.User ? ResourcePermissions.Generated
        : strategyFactory.GetResourceLoadStrategy(resource.FormatId) is Toucan.Core.Contracts.IResourceCapabilities provider
            ? provider.Permissions(resource) : ResourcePermissions.Editable;

    internal static void RestoreStates(ProjectSettings settings, IEnumerable<TranslationItem> translations, Func<TranslationItem, IResourceMetadataAuthority?>? metadataAuthority = null)
    {
        var states = settings.TranslationStates.ToDictionary(s => (s.Language, s.Key));
        var identities = settings.TranslationStates.Where(s => s.Identity != null)
            .GroupBy(s => (s.Language, s.Identity!.Address)).ToDictionary(g => g.Key, g => g.First());
        foreach (var item in translations)
            if ((item.Identity != null && identities.TryGetValue((item.Language, item.Identity.Address), out var state)) ||
                (states.TryGetValue((item.Language, item.Namespace), out state) && state.Identity is null))
            {
                if (state.Identity is { } identity)
                {
                    item.Identity = identity;
                    item.Namespace = identity.Selector;
                }
                var authority = metadataAuthority?.Invoke(item);
                if (authority?.CommentsInResource != true) item.Comment = state.Comment;
                item.IsApproved = state.Approved;
                foreach (var entry in state.Data.Where(p => (item.Identity is null || p.Key != NestedJsonParser.JsonScalarKey) && authority?.OwnsMetadata(p.Key) != true))
                    (item.FormatData ??= new())[entry.Key] = entry.Value;
            }
    }

    public static ResourceOptions EffectiveOptions(ProjectSettings settings) => ResourceOptions.Resolve(settings.ResourceOptions,
        new ResourceOptions
        {
            InputEncoding = settings.InputEncoding, OutputEncoding = settings.TextEncoding, SaveEmptyTranslations = settings.SaveEmptyTranslations,
            Newline = settings.LineEnding switch { null => null, "LF" => ResourceNewline.Lf, "CRLF" => ResourceNewline.CrLf,
                _ => throw new FormatException("[lineEnding]: unsupported project newline override.") }
        }, null, null);

    private static ProjectSettings FromCompatibility(CompatibilityProject imported) => new()
    {
        ImportDiagnostics = imported.Diagnostics, Name = imported.Name, ProjectPath = imported.ProjectPath, Framework = imported.Framework,
        SaveFormat = imported.SaveFormat, PrimaryLanguage = imported.PrimaryLanguage, Languages = imported.Languages,
        TranslationPackages = imported.TranslationPackages, ResourceDocuments = imported.ResourceDocuments,
        ResourceOptions = imported.ResourceOptions, Compatibility = imported.Compatibility,
        SaveEmptyTranslations = imported.SaveEmptyTranslations, InputEncoding = imported.InputEncoding,
        TranslationOrder = imported.TranslationOrder, CopyTemplates = imported.CopyTemplates
    };

    public ProjectSettings CreateProject(string folder, IEnumerable<string> languages, string formatId = FormatIds.Json, bool createManifest = true, string? name = null)
    {
        Directory.CreateDirectory(folder);

        var langList = languages.ToList();
        foreach (var language in langList)
            CreateLanguage(folder, language, formatId);

        var settings = new ProjectSettings
        {
            Name = name ?? Path.GetFileName(folder),
            ProjectPath = folder,
            PrimaryLanguage = langList.FirstOrDefault() ?? "en-US",
            Languages = langList,
            SaveFormat = formatId,
            DefaultPathResolver = ResolveDefaultPath
        };

        if (createManifest)
            settings.Save();

        return settings;
    }

    public void Save(ProjectSettings project, List<NsTreeItem> items, IEnumerable<TranslationItem> translations)
    {
        var files = project.ResourceDocuments.Select(d => ResourceFilePath.Resolve(project.ProjectPath, d.Resource.RelativePath)).ToList();
        var transaction = files.Count > 0
            ? SaveTransaction.BeginIfNeeded(project.ProjectPath, files.Append(Path.Combine(project.ProjectPath, "toucan.tproj"))) : null;
        var descriptors = project.ResourceDocuments.Select(d => d.Resource).ToList();
        var detected = project.ResourceDocuments.Select(d => d.DetectedOptions).ToList();
        var snapshots = project.ResourceDocuments.Select(d => new Dictionary<string, System.Text.Json.JsonElement>(d.Data)).ToList();
        try { SaveProject(project, items, translations); transaction?.Commit(); }
        catch
        {
            for (var i = 0; i < snapshots.Count; i++)
            { project.ResourceDocuments[i].Data = snapshots[i]; project.ResourceDocuments[i].Resource = descriptors[i]; project.ResourceDocuments[i].DetectedOptions = detected[i]; }
            transaction?.Rollback();
            throw;
        }
    }

    private void SaveProject(ProjectSettings project, List<NsTreeItem> items, IEnumerable<TranslationItem> translations)
    {
        if (project.ResourceSchemaVersion != 1) throw new FormatException("Cannot overwrite an unsupported resource schema.");
        var toSave = translations.ToList();
        if (project.ResourceDocuments.Count > 0) BindNewEntries(project, toSave);
        // Reverse alias mapping for file output
        if (project.LanguageAliases is { Count: > 0 })
        {
            // Build reverse map safely (last alias wins if multiple map to same file code)
            var reverse = new Dictionary<string, string>();
            foreach (var kv in project.LanguageAliases)
                reverse[kv.Value] = kv.Key;

            var list = toSave;
            foreach (var t in list)
                if (reverse.TryGetValue(t.Language, out var fileCode))
                    t.Language = fileCode;
            toSave = list;
        }

        try
        {
            Save(project.ProjectPath, project.SaveFormat, items, toSave, project.SaveEmptyTranslations ?? true, project.ResourceOptions.Layout == ResourceLayout.Literal ? "flat" : null, project);
            if (project.ResourceDocuments.Count == 0 && FormatIds.TryGetStyle(project.SaveFormat, out _) && (project.TextEncoding != null || project.LineEnding != null))
            {
                var files = toSave.ToLanguages().SelectMany(lang => GetLanguageFiles(project, lang));
                foreach (var file in files.Distinct(StringComparer.Ordinal))
                {
                    var utf8Properties = toSave.Any(i => i.FormatData?.GetValueOrDefault("properties.encoding") == "UTF-8" &&
                        Path.GetFullPath(Path.Combine(project.ProjectPath, i.FormatData.GetValueOrDefault("properties.file") ?? i.Language + ".properties")) == Path.GetFullPath(file));
                    ProjectTextFormat.Apply(file, project.TextEncoding, project.LineEnding,
                        project.SaveFormat == FormatIds.JavaProperties && !utf8Properties ? System.Text.Encoding.Latin1 : null);
                }
            }
        }
        finally
        {
            // Restore display codes for in-memory state, also when the write failed
            if (project.LanguageAliases is { Count: > 0 })
                foreach (var t in (IEnumerable<TranslationItem>)toSave)
                    if (project.LanguageAliases.TryGetValue(t.Language, out var mapped))
                        t.Language = mapped;
        }

        foreach (var importer in compatibilityImporters ?? []) importer.CaptureMetadata(project.Compatibility, toSave);
        project.TranslationStates = toSave.GroupBy(i => (i.Language, i.Namespace)).Select(g => g.First())
            .Select(i => new TranslationState { Identity = i.Identity, Language = i.Language, Key = i.Namespace, Comment = i.Comment, Approved = i.IsApproved,
                Data = i.FormatData?.Where(p => i.Identity is null || p.Key != NestedJsonParser.JsonScalarKey)
                    .ToDictionary(p => p.Key, p => p.Value) ?? [] }).ToList();
        // Update project manifest with current language list
        project.Languages = project.ResourceDocuments.Count > 0
            ? project.ResourceDocuments.Select(d => project.LanguageAliases?.GetValueOrDefault(d.Resource.Language) ?? d.Resource.Language).Distinct().ToList()
            : toSave.ToLanguages().ToList();
        project.Save();
    }

    public void CreateLanguage(string folder, string language, string formatId = FormatIds.Json)
    {
        var translations = new List<TranslationItem>
        {
            new() { Namespace = "app", Value = string.Empty, Language = language }
        };

        var context = new SaveContext
        {
            LanguageDictionary = new Dictionary<string, IEnumerable<TranslationItem>> { { language, translations } },
            NsTreeItems = [],
            Languages = [language]
        };

        var strategy = strategyFactory.GetSaveStrategy(formatId);
        if (strategy != null)
            strategy.Save(folder, context);
        else if (!FormatIds.TryGetStyle(formatId, out _))
            throw new FormatUnavailableException(formatId);
        else
            fileService.Save(folder, language + ".json", new Dictionary<string, string> { { "app", "" } });
    }

    public List<TranslationItem> Load(string folder)
    {
        return Load(folder, FormatIds.Json);
    }

    private List<TranslationItem> Load(string folder, string formatId, bool useManifest = true, string? inputEncoding = null)
    {
        if (string.IsNullOrEmpty(folder)) return [];

        var strategy = strategyFactory.GetLoadStrategy(formatId);

        // A format we know nothing about (e.g. its plugin is missing) must not fall through to the manifest or
        // JSON loaders: they would misread the files, and a later save could overwrite them.
        // Built-in formats without a loader keep the legacy JSON fallback below.
        if (strategy == null && !FormatIds.TryGetStyle(formatId, out _))
            throw new FormatUnavailableException(formatId);

        var variant = modeResolver.Resolve(folder);
        if (useManifest && formatId == FormatIds.Json && variant == ProjectTypeVariant.ConfigManifest)
        {
            var loader = strategyFactory.GetManifestLoadStrategy();
            if (loader != null)
            {
                var items = loader.Load(folder).ToList();
                if (items.Count > 0) return items;
            }
        }

        // Use the detected format's load strategy (not just Json fallback)
        if (strategy != null) return strategy.Load(folder, inputEncoding).ToList();

        // Final fallback: try Json
        return (strategyFactory.GetLoadStrategy(FormatIds.Json)?.Load(folder) ?? []).ToList();
    }

    public void Save(string path, string formatId, List<NsTreeItem> items, IEnumerable<TranslationItem> translations)
        => Save(path, formatId, items, translations, true, null);

    private void Save(string path, string formatId, List<NsTreeItem> items, IEnumerable<TranslationItem> translations, bool saveEmpty, string? jsonLayout, ProjectSettings? project = null)
    {
        var strategy = strategyFactory.GetSaveStrategy(formatId)
            ?? throw new FormatUnavailableException(formatId);

        // Saving rewrites the files; refuse when that would silently drop content the format cannot write back.
        if (project?.ResourceDocuments.Count is not > 0 && Directory.Exists(path) && strategy.FindUnsupportedConstructs(path) is { Count: > 0 } unsafeConstructs)
            throw new FormatSaveBlockedException(formatId, unsafeConstructs);

        var context = new SaveContext
        {
            Documents = project?.ResourceDocuments ?? [],
            ResourceOptions = project is not null ? EffectiveOptions(project) : new(),
            SaveEmptyTranslations = saveEmpty,
            JsonLayout = jsonLayout,
            LanguageDictionary = translations.ToLanguageDictionary(),
            NsTreeItems = items ?? [],
            Languages = translations.ToLanguages().ToList()
        };

        if (context.Documents.Count == 0) { strategy.Save(path, context); return; }
        // Preflight every destination/capability before the first write; use the existing journal.
        foreach (var doc in context.Documents)
        {
            ResourceFilePath.Resolve(path, doc.Resource.RelativePath);
            var resolvedOptions = ResourceOptions.Resolve(doc.Resource.Options, context.ResourceOptions, doc.DetectedOptions, null);
            ResourceText.Validate(resolvedOptions, doc.Resource.RelativePath);
            if (strategyFactory.GetResourceSaveStrategy(doc.Resource.FormatId) is null)
                throw new FormatException($"{doc.Resource.RelativePath} [format]: exact writing is unsupported for '{doc.Resource.FormatId}'.");
        }
        var entries = context.LanguageDictionary.Values.SelectMany(v => v).ToList();
        foreach (var doc in context.Documents)
        {
            var result = strategyFactory.WriteResource(new(path, doc, entries.Where(i => i.Identity?.ResourceId == doc.Resource.Id).ToList(),
                ResourceOptions.Resolve(context.ResourceOptions, new ResourceOptions { SaveEmptyTranslations = context.SaveEmptyTranslations }, null, null)));
            if (result.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error))
                throw new FormatException(string.Join("; ", result.Diagnostics.Select(d => $"{d.RelativePath} [{d.Field}]: {d.Message}")));
        }
    }

    public string GetDefaultFilePath(ProjectSettings settings, string language) =>
        settings.LanguageFilePaths?.TryGetValue(language, out var custom) == true
            ? custom
            : ResolveDefaultPath(settings.SaveFormat, language);

    public IReadOnlyList<string> GetLanguageFiles(ProjectSettings settings, string language)
    {
        if (settings.ResourceDocuments.Count > 0)
            return settings.ResourceDocuments.Where(d => d.Resource.Language == language)
                .Select(d => ResourceFilePath.Resolve(settings.ProjectPath, d.Resource.RelativePath)).ToList();
        if (settings.LanguageFilePaths?.TryGetValue(language, out var custom) == true)
            return [Path.IsPathRooted(custom) ? custom : Path.Combine(settings.ProjectPath, custom.Replace('/', Path.DirectorySeparatorChar))];

        var strategy = strategyFactory.GetSaveStrategy(settings.SaveFormat);
        return strategy?.LanguageFiles(settings.ProjectPath, language)
            ?? [Path.Combine(settings.ProjectPath, language + ".json")];
    }

    private string ResolveDefaultPath(string formatId, string language) =>
        strategyFactory.GetSaveStrategy(formatId)?.DefaultFilePath(language) ?? $"{language}.json";

    private List<TranslationItem> LoadResources(ProjectSettings settings, CancellationToken ct)
    {
        var resources = settings.ResourceDocuments.Select(d => d.Resource).ToList();
        if (resources.Count == 0 && settings.TranslationPackages.Count > 0)
            resources = settings.TranslationPackages.SelectMany(p => p.TranslationUrls.Select(u => new ResourceDescriptor
            {
                Id = u.Path.Replace('\\', '/'), RelativePath = u.Path, Package = p.Name, Language = u.Language, FormatId = settings.SaveFormat,
                SelectorPrefix = settings.TranslationPackages.Count > 1 ? ResourceEntryOperations.Escape(p.Name) + "." : "",
                Options = settings.ResourceOptions
            })).ToList();
        if (resources.Count == 0 && strategyFactory.GetLoadStrategy(settings.SaveFormat) is IResourceDiscoveryStrategy discovery)
            resources = discovery.DiscoverResources(settings.ProjectPath).ToList();
        var previous = settings.ResourceDocuments.ToDictionary(d => d.Resource.Id);
        var documents = new List<ResourceDocumentState>();
        var result = new List<TranslationItem>();
        foreach (var resource in resources)
        {
            ResourceReadResult loaded;
            try { loaded = strategyFactory.ReadResource(new ResourceReadRequest(settings.ProjectPath, resource, EffectiveOptions(settings), ct)); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.Xml.XmlException or FormatException or FileNotFoundException && SaveTransaction.FindInterrupted(settings.ProjectPath) != null)
            {
                // An interrupted write must still open far enough to offer journal restoration.
                loaded = new ResourceReadResult([], new ResourceDocumentState { Resource = resource }, []);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.Xml.XmlException or FormatException or IOException or ArgumentException or System.Text.DecoderFallbackException)
            {
                throw new CompatibilityImportException([new ResourceDiagnostic("resource.content-invalid", ex.Message,
                    ResourceDiagnosticSeverity.Error, resource.RelativePath, "content")]);
            }
            if (loaded.Diagnostics.Any(d => d.Severity == ResourceDiagnosticSeverity.Error) || loaded.Document is null)
                throw new FormatException(string.Join("; ", loaded.Diagnostics.Select(d => $"{d.RelativePath} [{d.Field}]: {d.Message}")));
            var document = loaded.Document;
            if (previous.TryGetValue(resource.Id, out var old))
            {
                document.ExtensionData = old.ExtensionData;
                foreach (var pair in old.Data.Where(p => !document.Data.ContainsKey(p.Key))) document.Data[pair.Key] = pair.Value;
            }
            documents.Add(document);
            foreach (var item in loaded.Entries)
            {
                var data = item.FormatData ??= new();
                if (resource.FormatId == FormatIds.Json) data["json.file"] = resource.RelativePath;
                result.Add(item);
            }
        }
        ResourceJsonCodec.ResolveSelectors(result);
        settings.ResourceDocuments = documents;
        if (settings.Languages.Count == 0) settings.Languages = resources.Select(r => r.Language).Distinct().ToList();
        return result;
    }

    private static void BindNewEntries(ProjectSettings settings, IEnumerable<TranslationItem> entries)
    {
        var all = entries.ToList();
        foreach (var item in all.Where(i => i.Identity is null || !settings.ResourceDocuments.Any(d => d.Resource.Id == i.Identity.ResourceId)))
        {
            if (item.Identity is { ResourceId.Length: > 0 }) throw new InvalidOperationException("The entry's resource destination is not registered in this project.");
            var candidates = settings.ResourceDocuments.Where(d => (settings.LanguageAliases?.GetValueOrDefault(d.Resource.Language) ?? d.Resource.Language) == item.Language &&
                (d.Resource.SelectorPrefix.Length == 0 || item.Namespace.StartsWith(d.Resource.SelectorPrefix, StringComparison.Ordinal))).ToList();
            if (candidates.Count != 1) throw new InvalidOperationException("Choose a resource destination before saving this key.");
            var resource = candidates[0].Resource;
            var key = item.Namespace.StartsWith(resource.SelectorPrefix, StringComparison.Ordinal) ? item.Namespace[resource.SelectorPrefix.Length..] : item.Namespace;
            var source = all.FirstOrDefault(i => i.Namespace == item.Namespace && i.Identity?.Package == resource.Package)?.Identity;
            item.Identity = new ResourceEntryIdentity { ResourceId = resource.Id, Package = resource.Package, PackagePrefix = resource.SelectorPrefix,
                Selector = item.Namespace, Path = source is not null ? [.. source.Path] : ResourceText.NewPath(key, ResourceOptions.Resolve(resource.Options, EffectiveOptions(settings), null, null), resource.FormatId is FormatIds.Arb or FormatIds.JavaProperties or FormatIds.Resx) };
        }
    }

}
