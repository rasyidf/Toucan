namespace Toucan.Core.Contracts;

/// <summary>
/// A framework profile encapsulates the conventions of a specific i18n framework:
/// how translation files are discovered, where languages are encoded in paths,
/// and what metadata accompanies translations.
/// </summary>
public interface IFrameworkProfile
{
    /// <summary>Unique identifier (e.g., "i18next", "android", "generic-json").</summary>
    string Id { get; }

    /// <summary>Human-readable name for UI display.</summary>
    string DisplayName { get; }

    /// <summary>Default file format this framework uses.</summary>
    Models.SaveStyles DefaultFormat { get; }

    /// <summary>Glob patterns for file discovery (e.g., "locales/*&#47;*.json").</summary>
    IEnumerable<string> FilePatterns { get; }

    /// <summary>Discover translation files in a folder using this framework's conventions.</summary>
    IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder);

    /// <summary>Extract language code from a relative file path. Returns null if not determinable.</summary>
    string? ExtractLanguage(string relativePath);

    /// <summary>Generate the output path for a language file when saving.</summary>
    string GetFilePath(string rootFolder, string language, string? package = null);

    /// <summary>Score how likely this profile matches a given folder (0 = no match, higher = better).</summary>
    int DetectionScore(string rootFolder);
}

/// <summary>A translation file discovered by a framework profile.</summary>
public record DiscoveredFile(string AbsolutePath, string RelativePath, string Language, string? Package = null);
