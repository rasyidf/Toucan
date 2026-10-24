using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.ViewModels;

public sealed partial class ResourceMappingDraft(ResourceDocumentState document) : ObservableObject
{
    public ResourceDocumentState Original { get; } = document;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private string relativePath = document.Resource.RelativePath;
    [ObservableProperty] private string package = document.Resource.Package;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Label))] private string language = document.Resource.Language;
    [ObservableProperty] private string? dialect = document.Resource.Dialect;
    [ObservableProperty] private string selectorPrefix = document.Resource.SelectorPrefix;
    public bool ReloadInput { get; set; }
    public ResourceOptions Options { get; set; } = document.Resource.Options with { };
    public string Label => $"{Language} · {RelativePath}";
    public ResourceDocumentState Build() => new()
    {
        Resource = Original.Resource with { RelativePath = RelativePath, Package = Package, Language = Language, SelectorPrefix = SelectorPrefix, Options = Options, Dialect = Dialect },
        Data = new(Original.Data), DetectedOptions = Original.DetectedOptions, ExtensionData = Original.ExtensionData
    };
}

public sealed class ResourceSettingsViewModel
{
    private readonly ProjectSettings _current;
    private readonly ProjectSettings _draft;
    private readonly IReadOnlyList<TranslationItem> _entries;
    private readonly bool _workspaceHasEdits;
    private readonly Dictionary<string, string> _conversions = [];
    private readonly Dictionary<string, string> _externalCopies = [];
    public event EventHandler? OptionsChanged;
    public void RefreshEffectiveOptions() => OptionsChanged?.Invoke(this, EventArgs.Empty);
    private ProjectSettings TextSettings(ResourceOptions options) => new()
    {
        ResourceOptions = options, InputEncoding = _draft.InputEncoding, TextEncoding = _draft.TextEncoding,
        LineEnding = _draft.LineEnding, SaveEmptyTranslations = _draft.SaveEmptyTranslations, TranslationOrder = _draft.TranslationOrder
    };
    public ResourceOptions Effective(ResourceMappingDraft? row) => ResourceOptions.Resolve(row?.Options,
        ProjectService.EffectiveOptions(TextSettings(Defaults)), row?.Original.DetectedOptions, null);
    public string Origin(ResourceMappingDraft? row, string field)
    {
        var property = typeof(ResourceOptions).GetProperty(field)!;
        if (row != null && property.GetValue(row.Options) != null) return "resource";
        if (property.GetValue(Defaults) != null) return "project";
        if (property.GetValue(ProjectService.EffectiveOptions(TextSettings(new()))) != null) return "legacy project";
        return row != null && property.GetValue(row.Original.DetectedOptions) != null ? "detected" : "format default";
    }
    public ITranslationStrategyFactory Factory { get; }
    public ObservableCollection<ResourceMappingDraft> Resources { get; } = [];
    public ResourceOptions Defaults { get; set; }
    public ResourceConfigurationPlan? AcceptedPlan { get; private set; }
    public ResourceSettingsViewModel(ProjectSettings draft, ITranslationStrategyFactory factory, IReadOnlyList<TranslationItem> entries, bool workspaceHasEdits = false)
    {
        _workspaceHasEdits = workspaceHasEdits;
        _current = ProjectConfiguration.Copy(draft); _draft = draft;
        Factory = factory; _entries = entries.Select(i => ResourceEntryOperations.Copy(i)).ToList();
        Defaults = draft.ResourceOptions with { };
        foreach (var document in draft.ResourceDocuments) Resources.Add(new(document));
    }
    public IEnumerable<ISaveStrategy> TargetFormats => Factory.SaveStrategies.Where(s => s is IResourceSaveStrategy &&
        Factory.GetResourceLoadStrategy(s.FormatId) != null && (s is not IResourceCapabilities c || c.Permissions(new() { FormatId = s.FormatId }).EditKeys));
    public IReadOnlySet<string> Fields(ResourceMappingDraft row) => IsProtected(row) ? new HashSet<string>() :
        Factory.GetResourceSaveStrategy(row.Original.Resource.FormatId) is IResourceOptionsProvider p ? p.SupportedOptions(row.Build().Resource) : new HashSet<string>();
    public bool IsExisting(ResourceMappingDraft row) => _current.ResourceDocuments.Any(d => d.Resource.Id == row.Original.Resource.Id);
    public bool IsProtected(ResourceMappingDraft row) => row.Original.Resource.Ownership != ResourceOwnership.User ||
        Factory.GetResourceLoadStrategy(row.Original.Resource.FormatId) is IResourceCapabilities c && !c.Permissions(row.Original.Resource).EditKeys;
    public ResourceMappingDraft AddTarget(string formatId)
    {
        var generated = Resources.FirstOrDefault(IsProtected);
        var resource = new ResourceDescriptor { FormatId = formatId, Language = "new-locale", RelativePath = Factory.GetSaveStrategy(formatId)!.DefaultFilePath("new-locale"),
            Package = generated?.Package ?? "main", SelectorPrefix = generated?.SelectorPrefix ?? "",
            Options = generated != null && formatId == "json" ? new() { Layout = ResourceLayout.Literal } : new(),
            Dialect = generated != null && formatId == "json" ? "formatjs-target" : null };
        var row = new ResourceMappingDraft(new() { Resource = resource }); Resources.Add(row); return row;
    }
    public ResourceMappingDraft CopyExternal(string formatId, string file)
    {
        var row = AddTarget(formatId); row.RelativePath = "imported/" + Path.GetFileName(file);
        _externalCopies[row.Original.Resource.Id] = Path.GetFullPath(file); return row;
    }
    public ResourceMappingDraft Convert(ResourceMappingDraft source, string formatId)
    {
        if (IsProtected(source)) throw new InvalidOperationException("Generated sources cannot be converted.");
        var writer = Factory.GetSaveStrategy(formatId) ?? throw new FormatException("Format is unavailable.");
        var row = new ResourceMappingDraft(new() { Resource = new() { FormatId = formatId, Language = source.Language, Package = source.Package,
            SelectorPrefix = source.SelectorPrefix, RelativePath = "converted/" + writer.DefaultFilePath(source.Language) } });
        _conversions[row.Original.Resource.Id] = source.Original.Resource.Id;
        Resources.Remove(source); Resources.Add(row); return row;
    }
    public ResourceConfigurationPlan Preview()
    {
        var proposed = ProjectConfiguration.Copy(_draft);
        proposed.ResourceDocuments = Resources.Select(r => r.Build()).ToList();
        proposed.ResourceOptions = Defaults;
        // Typed preferences are authoritative; preserve legacy preferences as lower-priority fallback.
        AcceptedPlan = new ResourceConfigurationPlanner(Factory).Plan(_current, proposed, _entries, reloadInputs: Resources.Where(r => r.ReloadInput).Select(r => r.Original.Resource.Id).ToHashSet(), workspaceHasEdits: _workspaceHasEdits, conversions: _conversions, externalCopies: _externalCopies);
        return AcceptedPlan;
    }
    public void ApplyDraft()
    {
        var plan = Preview();
        if (!plan.CanApply) throw new FormatException(string.Join("\n", plan.Diagnostics.Select(d => $"{d.RelativePath}: {d.Message}")));
        _draft.ResourceOptions = plan.Settings.ResourceOptions;
        _draft.ResourceDocuments = plan.Settings.ResourceDocuments;
        _draft.Compatibility = plan.Settings.Compatibility;
        _draft.SaveFormat = plan.Settings.SaveFormat;
        _draft.Languages = plan.Settings.Languages;
    }
}
