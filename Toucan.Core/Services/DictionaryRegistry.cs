using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Toucan.Core.Models;
using WeCantSpell.Hunspell;

namespace Toucan.Core.Services;

/// <summary>Local, user-owned dictionary store. Imports never grant redistribution approval.</summary>
public sealed partial class DictionaryRegistry
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _root;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<WordList>>> _loaded = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loadSlots = new(2);
    private RegistryState _state = new();
    private sealed class RegistryState
    {
        public List<DictionaryPack> Packs { get; set; } = [];
        public Dictionary<string, List<string>> Words { get; set; } = [];
    }
    static DictionaryRegistry() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    public string LoadStatus { get; private set; } = "";
    public event EventHandler? Changed;
    public DictionaryRegistry(string? root = null)
    {
        _root = root ?? Path.Combine(UserDataFolder.Root, "Toucan", "dictionaries");
        try
        {
            var file = Path.Combine(_root, "registry.json");
            if (File.Exists(file))
            {
                if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new FormatException("Dictionary registry exceeds 2 MiB.");
                _state = JsonSerializer.Deserialize<RegistryState>(File.ReadAllText(file)) ?? new();
                _state.Packs ??= []; _state.Words ??= [];
                if (_state.Packs.Count > 64 || _state.Packs.Any(p => p == null || p.Id == null || !IdPattern().IsMatch(p.Id) || p.Locales == null || p.Locales.Count == 0 || p.Locales.Count > 32 || p.AffixSha256 == null || p.DictionarySha256 == null || !IdPattern().IsMatch(p.AffixSha256) || !IdPattern().IsMatch(p.DictionarySha256))) throw new FormatException("Dictionary registry has invalid pack IDs or too many packs.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        { LoadStatus = "Dictionary registry unavailable: " + ex.Message; _state = new(); }
    }
    public IReadOnlyList<DictionaryPack> Packs { get { lock (_sync) return _state.Packs.ToList(); } }
    public static string NormalizeLocale(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) throw new FormatException("Choose a valid locale.");
        var normalized = locale.Replace('_', '-').Trim().ToLowerInvariant();
        if (normalized.Length > 48 || !LocalePattern().IsMatch(normalized)) throw new FormatException("Use a locale such as en, en-US or pt-BR.");
        return normalized;
    }
    public (DictionaryPack? Pack, string Status) Resolve(string locale, SpellingSettings settings)
    {
        locale = NormalizeLocale(locale);
        lock (_sync)
        {
            var target = settings.Fallbacks.TryGetValue(locale, out var fallback) ? NormalizeLocale(fallback) : locale;
            if (settings.Assignments.TryGetValue(locale, out var assigned))
            {
                var selected = _state.Packs.FirstOrDefault(p => p.Id == assigned && p.Locales.Contains(target, StringComparer.OrdinalIgnoreCase));
                return (selected, selected == null ? "Assigned dictionary is missing or does not support the selected locale." : $"{locale} → {target} · {selected.Name}");
            }
            var matching = _state.Packs.Where(p => p.Locales.Contains(target, StringComparer.OrdinalIgnoreCase)).ToList();
            return matching.Count == 1 ? (matching[0], $"{locale} → {target} · {matching[0].Name}") :
                (null, matching.Count == 0 ? $"No local dictionary for {locale}. Import a pack or choose an explicit fallback." : $"Multiple dictionaries support {locale}; choose one in Project Properties.");
        }
    }
    public Task<IReadOnlyList<DictionaryPack>> ImportFolderAsync(string folder, CancellationToken cancellationToken = default) => ImportFolderAsync(folder, null, cancellationToken);
    public async Task<IReadOnlyList<DictionaryPack>> ImportFolderAsync(string folder, string? localeOverride, CancellationToken cancellationToken = default)
    {
        folder = Path.GetFullPath(folder);
        var registrations = new List<(string Aff, string Dic, List<string> Locales)>();
        var xcus = Directory.EnumerateFiles(folder, "*.xcu").Take(17).ToList();
        if (xcus.Count > 16) throw new FormatException("At most 16 registration files are supported per import.");
        foreach (var xcu in xcus)
        {
            using var stream = new MemoryStream(ReadBounded(ResourceFilePath.Resolve(folder, Path.GetFileName(xcu)), 1024 * 1024));
            using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
            var xml = XDocument.Load(reader);
            foreach (var node in xml.Descendants().Where(n => n.Name.LocalName == "node"))
            {
                string Value(string name) => node.Elements().FirstOrDefault(p => p.Name.LocalName == "prop" && p.Attributes().Any(a => a.Name.LocalName == "name" && a.Value == name))?.Value.Trim() ?? "";
                if (Value("Format") != "DICT_SPELL") continue;
                var locations = Value("Locations").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Location).ToList();
                if (locations.Count != 2) throw new FormatException("DICT_SPELL requires exactly one .aff and one .dic location.");
                var aff = locations.SingleOrDefault(p => p.EndsWith(".aff", StringComparison.OrdinalIgnoreCase));
                var dic = locations.SingleOrDefault(p => p.EndsWith(".dic", StringComparison.OrdinalIgnoreCase));
                if (aff == null || dic == null) throw new FormatException("DICT_SPELL locations must include an .aff/.dic pair.");
                var locales = Value("Locales").Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries).Select(NormalizeLocale).Distinct().ToList();
                if (locales.Count == 0 || locales.Count > 32) throw new FormatException("DICT_SPELL needs 1–32 explicit locales.");
                registrations.Add((aff, dic, locales));
            }
        }
        // A registration file is authoritative: do not import hyphenation/thesaurus files by fallback.
        if (xcus.Count == 0)
            foreach (var dic in Directory.EnumerateFiles(folder, "*.dic").Take(65))
                registrations.Add((Path.GetFileNameWithoutExtension(dic) + ".aff", Path.GetFileName(dic), [NormalizeLocale(localeOverride ?? Path.GetFileNameWithoutExtension(dic))]));
        if (localeOverride != null && (registrations.Count != 1 || xcus.Count > 0)) throw new FormatException("A locale override applies only to one plain dictionary pair without registration files.");
        if (registrations.Count == 0 || registrations.Count > 64) throw new FormatException("Select a folder with 1–64 spelling dictionary pairs or DICT_SPELL registrations.");
        var pending = new List<(DictionaryPack Pack, byte[] Aff, byte[] Dic)>();
        long total = 0;
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var aff = ReadBounded(ResourceFilePath.Resolve(folder, registration.Aff), 4 * 1024 * 1024);
            var dic = ReadBounded(ResourceFilePath.Resolve(folder, registration.Dic), 32 * 1024 * 1024);
            total += aff.Length + dic.Length;
            if (total > 64 * 1024 * 1024) throw new FormatException("Dictionary import exceeds 64 MiB.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await LoadBytesAsync(aff, dic, timeout.Token).ConfigureAwait(false);
            var affHash = Hash(aff); var dicHash = Hash(dic);
            var id = Hash(System.Text.Encoding.UTF8.GetBytes(affHash + dicHash + string.Join(';', registration.Locales.Order(StringComparer.Ordinal))));
            pending.Add((new(id, Path.GetFileNameWithoutExtension(registration.Dic), registration.Locales, affHash, dicHash,
                "User-selected local import", "Unverified user import; not approved for redistribution", []), aff, dic));
        }
        var notices = Directory.EnumerateFiles(folder).Where(IsNotice).Take(33).ToList();
        if (notices.Count > 32) throw new FormatException("At most 32 local notice files are supported.");
        var noticeBytes = notices.ToDictionary(p => Path.GetFileName(p), p => ReadBounded(ResourceFilePath.Resolve(folder, Path.GetFileName(p)), 1024 * 1024));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_state.Packs.Select(p => p.Id).Union(pending.Select(p => p.Pack.Id)).Count() > 64) throw new FormatException("Remove an unused dictionary before importing more than 64 packs.");
            foreach (var item in pending)
            {
                var root = Path.Combine(_root, item.Pack.Id); Directory.CreateDirectory(root);
                File.WriteAllBytes(Path.Combine(root, "dictionary.aff"), item.Aff); File.WriteAllBytes(Path.Combine(root, "dictionary.dic"), item.Dic);
                var noticeRoot = Path.Combine(root, "notices"); Directory.CreateDirectory(noticeRoot);
                foreach (var notice in noticeBytes) File.WriteAllBytes(Path.Combine(noticeRoot, notice.Key), notice.Value);
                _state.Packs.RemoveAll(p => p.Id == item.Pack.Id);
                _state.Packs.Add(item.Pack with { Notices = noticeBytes.Keys.ToList(), NoticeHashes = noticeBytes.ToDictionary(p => p.Key, p => Hash(p.Value)) });
                _loaded.TryRemove(item.Pack.Id, out _);
            }
            Persist();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return pending.Select(p => p.Pack).ToList();
        static string Location(string path)
        {
            path = Uri.UnescapeDataString(path);
            if (path.StartsWith("%origin%/", StringComparison.Ordinal)) path = path[9..];
            if (path.Contains('%') || Uri.TryCreate(path, UriKind.Absolute, out _)) throw new FormatException("Dictionary locations must be relative to the selected folder.");
            return path;
        }
    }
    public void Remove(string id)
    {
        lock (_sync) { _state.Packs.RemoveAll(p => p.Id == id); Persist(); _loaded.TryRemove(id, out _); }
        // Keep imported files available for replacement/recovery; no recursive deletion from registry input.
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public IReadOnlyList<string> UserWords(string locale)
    { lock (_sync) return _state.Words.GetValueOrDefault(NormalizeLocale(locale))?.ToList() ?? []; }
    public void SetUserWords(string locale, IEnumerable<string> words)
    {
        var list = words.Select(w => w.Trim()).Where(w => w.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (list.Count > 10000 || list.Any(w => w.Length > 128 || w.Any(char.IsWhiteSpace))) throw new FormatException("Use at most 10,000 individual words, each at most 128 characters.");
        lock (_sync) { _state.Words[NormalizeLocale(locale)] = list; Persist(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task<WordList> LoadAsync(DictionaryPack pack, CancellationToken ct)
    {
        var lazy = _loaded.GetOrAdd(pack.Id, _ => new Lazy<Task<WordList>>(async () =>
        {
            await _loadSlots.WaitAsync().ConfigureAwait(false);
            try
            {
                var root = ResourceFilePath.Resolve(_root, pack.Id);
                var aff = ReadBounded(ResourceFilePath.Resolve(root, "dictionary.aff"), 4 * 1024 * 1024);
                var dic = ReadBounded(ResourceFilePath.Resolve(root, "dictionary.dic"), 32 * 1024 * 1024);
                if (Hash(aff) != pack.AffixSha256 || Hash(dic) != pack.DictionarySha256) throw new FormatException("Imported dictionary hashes changed. Remove and reimport the pack.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                return await LoadBytesAsync(aff, dic, timeout.Token).ConfigureAwait(false);
            }
            finally { _loadSlots.Release(); }
        }));
        try { return await lazy.Value.WaitAsync(ct).ConfigureAwait(false); }
        catch { if (lazy.IsValueCreated && (lazy.Value.IsFaulted || lazy.Value.IsCanceled)) _loaded.TryRemove(pack.Id, out _); throw; }
    }
    private static async Task<WordList> LoadBytesAsync(byte[] aff, byte[] dic, CancellationToken ct)
    {
        using var affStream = new MemoryStream(aff); using var dicStream = new MemoryStream(dic);
        var dictionary = await WordList.CreateFromStreamsAsync(dicStream, affStream, ct).ConfigureAwait(false);
        if (dictionary.IsEmpty) throw new FormatException("Dictionary contains no usable words.");
        if (dictionary.Affix.Warnings.Count > 0) throw new FormatException("Dictionary parse findings: " + string.Join("; ", dictionary.Affix.Warnings.Take(8)));
        return dictionary;
    }
    private static bool IsNotice(string path)
    {
        var name = Path.GetFileName(path);
        return new[] { ".txt", ".md", ".lic", ".license", ".xcu" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) ||
            new[] { "LICENSE", "COPYING", "COPYRIGHT", "NOTICE", "README" }.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
    private void Persist() { Directory.CreateDirectory(_root); AtomicFile.WriteAllText(Path.Combine(_root, "registry.json"), JsonSerializer.Serialize(_state, Json)); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] ReadBounded(string path, int limit)
    {
        using var file = File.OpenRead(path);
        if (file.Length > limit) throw new FormatException($"{Path.GetFileName(path)} exceeds its size limit.");
        using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = file.Read(buffer)) > 0) { if (output.Length + read > limit) throw new FormatException("Dictionary grew beyond its size limit."); output.Write(buffer, 0, read); }
        return output.ToArray();
    }
    [GeneratedRegex(@"^[a-f0-9]{64}$", RegexOptions.NonBacktracking)] private static partial Regex IdPattern();
    [GeneratedRegex(@"^[a-z]{2,3}(-[a-z0-9]{2,8})*$", RegexOptions.NonBacktracking)] private static partial Regex LocalePattern();
}
