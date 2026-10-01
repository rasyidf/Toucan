using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Handles comment persistence via JSON sidecar files for save formats
/// that do not support inline comments.
/// </summary>
public class CommentPersistenceService(ILogger<CommentPersistenceService> logger, ITranslationStrategyFactory? strategyFactory = null) : ICommentPersistenceService
{
    private const string SchemaVersion = "1.0";
    private const int MaxCommentLength = 2000;
    private const string SidecarSuffix = ".comments.json";

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public bool RequiresSidecar(string formatId) => FindStrategy(formatId)?.StoresCommentsInline != true;

    // Falls back to the built-ins when no factory was supplied (tests, plain construction).
    private ISaveStrategy? FindStrategy(string formatId) =>
        strategyFactory?.GetSaveStrategy(formatId) ?? BuiltInFormats.FindSaveStrategy(formatId);

    /// <inheritdoc />
    public void SaveComments(string folder, string formatId, IEnumerable<TranslationItem> translations)
    {
        if (!RequiresSidecar(formatId))
            return;

        var grouped = translations.GroupBy(t => t.Language);

        foreach (var languageGroup in grouped)
        {
            var language = languageGroup.Key;
            var comments = new Dictionary<string, string>();

            foreach (var item in languageGroup)
            {
                if (string.IsNullOrEmpty(item.Comment))
                    continue;

                var comment = item.Comment.Length > MaxCommentLength
                    ? item.Comment[..MaxCommentLength]
                    : item.Comment;

                comments[item.Namespace] = comment;
            }

            var sidecarPath = GetSidecarPath(folder, formatId, language);

            if (comments.Count == 0)
            {
                // Remove sidecar file if no comments remain
                DeleteSidecarIfExists(sidecarPath);
                continue;
            }

            var document = new CommentSidecarDocument
            {
                SchemaVersion = SchemaVersion,
                Comments = comments,
            };

            try
            {
                var directory = Path.GetDirectoryName(sidecarPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(document, s_writeOptions);
                File.WriteAllText(sidecarPath, json);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Failed to write comment sidecar at {Path}", sidecarPath);
            }
        }
    }

    /// <inheritdoc />
    public void LoadComments(string folder, string formatId, IEnumerable<TranslationItem> translations)
    {
        if (!RequiresSidecar(formatId))
            return;

        // Build a lookup of valid (language, namespace) pairs from translations
        var translationList = translations as IList<TranslationItem> ?? translations.ToList();
        var byLanguage = translationList.GroupBy(t => t.Language);

        foreach (var languageGroup in byLanguage)
        {
            var language = languageGroup.Key;
            var sidecarPath = GetSidecarPath(folder, formatId, language);

            if (!File.Exists(sidecarPath))
                continue;

            Dictionary<string, string>? comments;
            try
            {
                var json = File.ReadAllText(sidecarPath);
                var document = JsonSerializer.Deserialize<CommentSidecarDocument>(json, s_readOptions);

                if (document?.Comments is null)
                {
                    logger.LogWarning("Comment sidecar at {Path} has no comments section, skipping", sidecarPath);
                    continue;
                }

                comments = document.Comments;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Malformed comment sidecar at {Path}, skipping", sidecarPath);
                continue;
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not read comment sidecar at {Path}, skipping", sidecarPath);
                continue;
            }

            // Build a set of valid namespaces for this language to discard orphans
            var validNamespaces = new HashSet<string>(languageGroup.Select(t => t.Namespace));

            // Restore comments onto matching items
            foreach (var item in languageGroup)
            {
                if (comments.TryGetValue(item.Namespace, out var comment))
                {
                    item.Comment = comment;
                }
            }

            // Orphaned entries (namespace in sidecar but not in translations) are simply not applied
            var orphanCount = comments.Keys.Count(k => !validNamespaces.Contains(k));
            if (orphanCount > 0 && logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Discarded {Count} orphaned comment entries from sidecar {Path}",
                    orphanCount, sidecarPath);
            }
        }
    }

    /// <summary>
    /// Determines the sidecar file path for a given language and format.
    /// The sidecar is placed alongside the translation file: &lt;translation-filename&gt;.comments.json
    /// </summary>
    private string GetSidecarPath(string folder, string formatId, string language)
    {
        var translationFileName = GetTranslationFileName(formatId, language);
        return Path.Combine(folder, translationFileName + SidecarSuffix);
    }

    /// <summary>
    /// Returns the translation file name (relative to project folder) the sidecar sits next to, as defined by the
    /// format's save strategy. Unknown formats fall back to the JSON convention.
    /// </summary>
    private string GetTranslationFileName(string formatId, string language) =>
        FindStrategy(formatId)?.CommentSidecarBase(language) ?? $"{language}.json";

    private void DeleteSidecarIfExists(string sidecarPath)
    {
        try
        {
            if (File.Exists(sidecarPath))
                File.Delete(sidecarPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to delete empty comment sidecar at {Path}", sidecarPath);
        }
    }

    /// <summary>JSON model for the comment sidecar file.</summary>
    private sealed class CommentSidecarDocument
    {
        public string SchemaVersion { get; set; } = "1.0";
        public Dictionary<string, string> Comments { get; set; } = [];
    }
}
