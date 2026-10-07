using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Infers the language a user most likely wants as the source language from the projects they opened lately.</summary>
public static class PreferredLanguageDetector
{
    /// <summary>
    /// The primary language of the most recently opened project that recorded one,
    /// or null when no recent project has a language yet.
    /// </summary>
    public static string? Detect(IEnumerable<Project> recent) =>
        recent
            .Where(p => !string.IsNullOrWhiteSpace(p.PrimaryLanguage))
            .OrderByDescending(p => p.LastOpened)
            .Select(p => p.PrimaryLanguage)
            .FirstOrDefault();

    /// <summary>The detected language when detection is on and found something, otherwise the configured default.</summary>
    public static string Resolve(string? configuredDefault, bool detectFromRecent, IEnumerable<Project> recent)
    {
        var fallback = string.IsNullOrWhiteSpace(configuredDefault) ? "en-US" : configuredDefault;
        return detectFromRecent ? Detect(recent) ?? fallback : fallback;
    }
}
