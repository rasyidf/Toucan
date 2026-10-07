using System.Text.Json;
using System.Text.Json.Serialization;
using Toucan.Core.Contracts;

namespace Toucan.Core.Services;

/// <summary>
/// Secrets in one file, <c>secrets.json</c> in the user's application-data folder next to the encryption key.
/// Each value is encrypted through <see cref="ISecureStorageService"/>; the keys are stored in clear so they can be listed.
/// The file is re-read when another process (the CLI, a second window) changes it.
/// </summary>
public sealed class SecretService : ISecretService
{
    private sealed class SecretFile
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("secrets")] public SortedDictionary<string, string> Secrets { get; set; } = new(StringComparer.Ordinal);
    }

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private readonly ISecureStorageService _protector;
    private readonly string _file;
    private readonly Lock _lock = new();
    private SortedDictionary<string, string> _secrets = new(StringComparer.Ordinal);
    private DateTime _loadedStamp = DateTime.MinValue;

    public SecretService(ISecureStorageService protector) : this(protector, Path.Combine(SecureStorageService.DefaultFolder, "secrets.json")) { }

    public SecretService(ISecureStorageService protector, string file)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _file = file;
    }

    /// <summary>Path of the secret store.</summary>
    public string FilePath => _file;

    public string? GetSecret(string key)
    {
        lock (_lock)
        {
            Refresh();
            if (!_secrets.TryGetValue(Normalize(key), out var cipher)) return null;
            var plain = _protector.Unprotect(cipher);
            return string.IsNullOrEmpty(plain) ? null : plain;
        }
    }

    public void SetSecret(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) { Remove(key); return; }
        lock (_lock)
        {
            Refresh();
            var k = Normalize(key);
            if (_secrets.TryGetValue(k, out var existing) && _protector.Unprotect(existing) == value) return;
            _secrets[k] = _protector.Protect(value);
            Write();
        }
    }

    public bool Remove(string key)
    {
        lock (_lock)
        {
            Refresh();
            if (!_secrets.Remove(Normalize(key))) return false;
            Write();
            return true;
        }
    }

    public IReadOnlyList<string> Keys(string prefix = "")
    {
        lock (_lock)
        {
            Refresh();
            return [.. _secrets.Keys.Where(k => k.StartsWith(Normalize(prefix), StringComparison.Ordinal))];
        }
    }

    public int RemoveAll(string prefix)
    {
        lock (_lock)
        {
            Refresh();
            var p = Normalize(prefix);
            var doomed = _secrets.Keys.Where(k => k.StartsWith(p, StringComparison.Ordinal)).ToList();
            foreach (var k in doomed) _secrets.Remove(k);
            if (doomed.Count > 0) Write();
            return doomed.Count;
        }
    }

    private static string Normalize(string key) => key.Trim().ToLowerInvariant();

    private void Refresh()
    {
        var stamp = File.Exists(_file) ? File.GetLastWriteTimeUtc(_file) : DateTime.MinValue;
        if (stamp == _loadedStamp) return;
        _loadedStamp = stamp;
        _secrets = new(StringComparer.Ordinal);
        if (stamp == DateTime.MinValue) return;

        try
        {
            var read = JsonSerializer.Deserialize<SecretFile>(File.ReadAllText(_file), s_json);
            foreach (var (k, v) in read?.Secrets ?? [])
                if (!string.IsNullOrEmpty(v)) _secrets[Normalize(k)] = v;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable store behaves as empty; it is only rewritten when a secret is saved.
        }
    }

    private void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = _file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new SecretFile { Secrets = _secrets }, s_json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, _file, overwrite: true);
        _loadedStamp = File.GetLastWriteTimeUtc(_file);
    }
}
