using Toucan.Core.Models;
using Toucan.Core.Services;

namespace Toucan.Avalonia.Services;

/// <summary>One project's spelling context. Session ignores never cross workspace boundaries.</summary>
public sealed class SpellingSession : IDisposable
{
    private readonly SpellingService _service;
    private readonly DictionaryRegistry _registry;
    private readonly Func<SpellingSettings> _settings;
    private readonly Action<string, string> _addProjectWord;
    private readonly Dictionary<string, HashSet<string>> _ignored = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Slots = new(2);
    private bool _disposed;
    public bool IsCurrent => !_disposed;
    public event EventHandler? Changed;
    public SpellingSession(SpellingService service, DictionaryRegistry registry, Func<SpellingSettings> settings, Action<string, string> addProjectWord)
    { _service = service; _registry = registry; _settings = settings; _addProjectWord = addProjectWord; _registry.Changed += OnChanged; }
    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    public void Refresh() => OnChanged(this, EventArgs.Empty);
    public async Task<SpellingResult> CheckAsync(string text, string locale, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = _settings();
        var settings = SpellingSettings.Copy(current);
        var normalized = DictionaryRegistry.NormalizeLocale(locale);
        var ignored = new HashSet<string>(_ignored.GetValueOrDefault(normalized) ?? [], StringComparer.Ordinal);
        await Slots.WaitAsync(ct);
        try { return await _service.CheckAsync(text, locale, settings, ignored, ct); }
        finally { Slots.Release(); }
    }
    public void Ignore(string word, string locale)
    {
        var normalized = DictionaryRegistry.NormalizeLocale(locale);
        if (!_ignored.TryGetValue(normalized, out var words)) _ignored[normalized] = words = new(StringComparer.Ordinal);
        if (words.Count < 10000) words.Add(word);
        Refresh();
    }
    public void Remember(string word, string locale, bool project)
    {
        if (project) _addProjectWord(locale, word);
        else _registry.SetUserWords(locale, _registry.UserWords(locale).Append(word));
        Refresh();
    }
    public void Dispose() { _disposed = true; _registry.Changed -= OnChanged; Changed = null; }
}
