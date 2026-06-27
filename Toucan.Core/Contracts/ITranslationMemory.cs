namespace Toucan.Core.Contracts;

/// <summary>
/// Translation Memory — stores past translations and provides fuzzy-match suggestions.
/// Persisted in ~/Documents/Toucan/translation-memory.json across projects.
/// </summary>
public interface ITranslationMemory
{
    /// <summary>Record a confirmed translation pair into memory.</summary>
    void Add(string sourceText, string targetText, string sourceLanguage, string targetLanguage);

    /// <summary>Bulk-add translations (e.g., after saving a project).</summary>
    void AddRange(IEnumerable<TranslationMemoryEntry> entries);

    /// <summary>Find matching translations for a source text. Returns ordered by similarity.</summary>
    IEnumerable<TranslationMemoryMatch> Search(string sourceText, string sourceLanguage, string targetLanguage, int maxResults = 5);

    /// <summary>Total entries in the memory.</summary>
    int Count { get; }

    /// <summary>Clear all entries.</summary>
    void Clear();
}

public record TranslationMemoryEntry(string SourceText, string TargetText, string SourceLanguage, string TargetLanguage, DateTime Timestamp);

public record TranslationMemoryMatch(string SourceText, string TargetText, double Similarity);
