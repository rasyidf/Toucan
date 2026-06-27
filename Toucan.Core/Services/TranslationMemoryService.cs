using System.IO;
using System.Text.Json;
using Toucan.Core.Contracts;

namespace Toucan.Core.Services;

/// <summary>
/// File-backed translation memory using Levenshtein-based fuzzy matching.
/// Stored in ~/Documents/Toucan/translation-memory.json.
/// ponytail: O(n) scan with early-exit on exact match. Upgrade path: trie or embedding index.
/// </summary>
public class TranslationMemoryService : ITranslationMemory
{
    private readonly List<TranslationMemoryEntry> _entries = [];
    private static readonly string s_path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan", "translation-memory.json");
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };
    private bool _dirty;

    public int Count => _entries.Count;

    public TranslationMemoryService()
    {
        Load();
    }

    public void Add(string sourceText, string targetText, string sourceLanguage, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(targetText)) return;

        // Deduplicate: update if exact source+langs match exists
        var existing = _entries.FindIndex(e =>
            e.SourceText == sourceText && e.SourceLanguage == sourceLanguage && e.TargetLanguage == targetLanguage);
        if (existing >= 0)
            _entries[existing] = new(sourceText, targetText, sourceLanguage, targetLanguage, DateTime.UtcNow);
        else
            _entries.Add(new(sourceText, targetText, sourceLanguage, targetLanguage, DateTime.UtcNow));

        _dirty = true;
        if (_entries.Count % 50 == 0) Save(); // periodic flush
    }

    public void AddRange(IEnumerable<TranslationMemoryEntry> entries)
    {
        foreach (var e in entries)
            Add(e.SourceText, e.TargetText, e.SourceLanguage, e.TargetLanguage);
        Save();
    }

    public IEnumerable<TranslationMemoryMatch> Search(string sourceText, string sourceLanguage, string targetLanguage, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return [];

        var candidates = _entries
            .Where(e => e.SourceLanguage == sourceLanguage && e.TargetLanguage == targetLanguage)
            .Select(e => new TranslationMemoryMatch(e.SourceText, e.TargetText, Similarity(sourceText, e.SourceText)))
            .Where(m => m.Similarity > 0.5)
            .OrderByDescending(m => m.Similarity)
            .Take(maxResults);

        return candidates;
    }

    public void Clear()
    {
        _entries.Clear();
        _dirty = true;
        Save();
    }

    /// <summary>Persist to disk. Called on dispose/app exit.</summary>
    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(s_path)!);
            // Keep max 10,000 entries (most recent)
            var toSave = _entries.Count > 10_000 ? _entries.OrderByDescending(e => e.Timestamp).Take(10_000).ToList() : _entries;
            File.WriteAllText(s_path, JsonSerializer.Serialize(toSave, s_json));
            _dirty = false;
        }
        catch { /* non-critical */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(s_path)) return;
            var json = File.ReadAllText(s_path);
            var loaded = JsonSerializer.Deserialize<List<TranslationMemoryEntry>>(json, s_json);
            if (loaded != null) _entries.AddRange(loaded);
        }
        catch { /* start empty on corruption */ }
    }

    /// <summary>Normalized Levenshtein similarity (0..1). 1 = exact match.</summary>
    private static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

        // Trigram-based similarity for speed (avoids O(n*m) Levenshtein on long strings)
        var triA = GetTrigrams(a.ToLowerInvariant());
        var triB = GetTrigrams(b.ToLowerInvariant());
        if (triA.Count == 0 || triB.Count == 0) return 0.0;

        var intersection = triA.Intersect(triB).Count();
        var union = triA.Union(triB).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> GetTrigrams(string s)
    {
        var set = new HashSet<string>();
        var padded = $"  {s} ";
        for (int i = 0; i <= padded.Length - 3; i++)
            set.Add(padded.Substring(i, 3));
        return set;
    }
}
