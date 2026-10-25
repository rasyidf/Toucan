namespace Toucan.Core.Models;

public sealed class SpellingSettings
{
    public static SpellingSettings Copy(SpellingSettings settings) => new()
    {
        Enabled = settings.Enabled, Assignments = new(settings.Assignments), Fallbacks = new(settings.Fallbacks),
        Words = settings.Words.ToDictionary(p => p.Key, p => p.Value?.ToList() ?? [])
    };
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Assignments { get; set; } = [];
    public Dictionary<string, string> Fallbacks { get; set; } = [];
    public Dictionary<string, List<string>> Words { get; set; } = [];
}
public sealed record DictionaryPack(string Id, string Name, IReadOnlyList<string> Locales, string AffixSha256, string DictionarySha256,
    string Origin, string License, IReadOnlyList<string> Notices)
{
    public IReadOnlyDictionary<string, string> NoticeHashes { get; init; } = new Dictionary<string, string>();
}
public sealed record SpellingIssue(int Offset, int Length, string Word, IReadOnlyList<string> Suggestions);
public sealed record SpellingResult(string Text, string Locale, string? PackId, string Status, IReadOnlyList<SpellingIssue> Issues, bool Available);
public sealed record SpellingWord(int Offset, int Length, string Word);
