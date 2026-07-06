using Toucan.Core.Models;
using Toucan.Core.Options;

namespace Toucan.Core.Services;

/// <summary>
/// Resolves effective project settings by merging project overrides with defaults.
/// Rule: project value wins if non-null; otherwise falls back to ProjectDefaults.
/// </summary>
public static class EffectiveSettingsResolver
{
    /// <summary>
    /// Returns the effective value: project override if set, otherwise the default.
    /// </summary>
    public static bool ResolveBool(bool? projectValue, bool defaultValue) =>
        projectValue ?? defaultValue;

    public static int ResolveInt(int? projectValue, int defaultValue) =>
        projectValue ?? defaultValue;

    public static string ResolveString(string? projectValue, string defaultValue) =>
        projectValue ?? defaultValue;

    public static List<string> ResolveList(List<string>? projectValue, List<string> defaultValue) =>
        projectValue ?? defaultValue;

    public static Dictionary<string, ValidationRuleConfig> ResolveValidationRules(
        Dictionary<string, ValidationRuleConfig>? projectRules,
        Dictionary<string, ValidationRuleConfig> defaultRules) =>
        projectRules ?? defaultRules;

    /// <summary>
    /// Builds an effective settings snapshot by merging project with defaults.
    /// Used by UI and validation pipeline.
    /// </summary>
    public static EffectiveProjectSettings Resolve(ProjectSettings project, ProjectDefaults defaults)
    {
        return new EffectiveProjectSettings
        {
            // Editor
            SaveEmptyTranslations = ResolveBool(project.SaveEmptyTranslations, defaults.SaveEmptyTranslations),
            TranslationOrder = ResolveString(project.TranslationOrder, defaults.TranslationOrder),
            CopyTemplates = ResolveList(project.CopyTemplates, defaults.CopyTemplates),
            CommentsEnabled = ResolveBool(project.CommentsEnabled, defaults.CommentsEnabled),

            // Translation
            DefaultProvider = ResolveString(project.DefaultProvider, defaults.DefaultProvider),
            Formality = ResolveString(project.Formality, defaults.Formality),
            Context = ResolveString(project.Context, defaults.Context),
            PreservePlaceholders = ResolveBool(project.PreservePlaceholders, defaults.PreservePlaceholders),
            PreviewBeforeApply = ResolveBool(project.PreviewBeforeApply, defaults.PreviewBeforeApply),

            // Validation
            ValidateOnSave = ResolveBool(project.ValidateOnSave, defaults.ValidateOnSave),
            ValidationRules = ResolveValidationRules(project.ValidationRules, defaults.ValidationRules),

            // Source Code
            SourceRoots = ResolveList(project.SourceRoots, defaults.SourceRoots),
            ExternalEditor = ResolveString(project.ExternalEditor, defaults.ExternalEditor),
            ScanExtensions = ResolveList(project.ScanExtensions, defaults.ScanExtensions),
            ExcludedDirectories = ResolveList(project.ExcludedDirectories, defaults.ExcludedDirectories),
            AutoScanOnOpen = ResolveBool(project.AutoScanOnOpen, defaults.AutoScanOnOpen),

            // Features
            AutoSaveEnabled = ResolveBool(project.AutoSaveEnabled, defaults.AutoSaveEnabled),
            AutoSaveIntervalSeconds = ResolveInt(project.AutoSaveIntervalSeconds, defaults.AutoSaveIntervalSeconds),
        };
    }
}

/// <summary>
/// Fully-resolved project settings snapshot (no nulls). Read-only consumption model.
/// </summary>
public class EffectiveProjectSettings
{
    // Editor
    public bool SaveEmptyTranslations { get; init; }
    public string TranslationOrder { get; init; } = "alphabetical";
    public List<string> CopyTemplates { get; init; } = [];
    public bool CommentsEnabled { get; init; }

    // Translation
    public string DefaultProvider { get; init; } = "Google";
    public string Formality { get; init; } = "Default";
    public string Context { get; init; } = "";
    public bool PreservePlaceholders { get; init; }
    public bool PreviewBeforeApply { get; init; }

    // Validation
    public bool ValidateOnSave { get; init; }
    public Dictionary<string, ValidationRuleConfig> ValidationRules { get; init; } = [];

    // Source Code
    public List<string> SourceRoots { get; init; } = [];
    public string ExternalEditor { get; init; } = "";
    public List<string> ScanExtensions { get; init; } = [];
    public List<string> ExcludedDirectories { get; init; } = [];
    public bool AutoScanOnOpen { get; init; }

    // Features
    public bool AutoSaveEnabled { get; init; }
    public int AutoSaveIntervalSeconds { get; init; } = 60;
}
