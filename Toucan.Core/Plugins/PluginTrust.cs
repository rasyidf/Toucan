using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

public enum PluginSignatureStatus
{
    /// <summary>No signature. Today every plugin is unsigned; this does not block loading.</summary>
    NotSigned,

    /// <summary>A signature was checked and is valid.</summary>
    Valid,

    /// <summary>A signature is present but does not verify. Such plugins are rejected.</summary>
    Invalid,
}

/// <summary>
/// Checks a plugin's publisher signature.
/// TODO(collaboration/auth milestone): signing becomes mandatory once the app has accounts and shared projects.
/// Until then the only implementation is <see cref="UnsignedPluginSignatureVerifier"/>, so the seam, the result
/// field and the UI wording exist but nothing is enforced.
/// </summary>
public interface IPluginSignatureVerifier
{
    PluginSignatureStatus Verify(PluginManifest manifest, string pluginDirectory);
}

/// <summary>Stub: reports every plugin as unsigned and never blocks.</summary>
public sealed class UnsignedPluginSignatureVerifier : IPluginSignatureVerifier
{
    public PluginSignatureStatus Verify(PluginManifest manifest, string pluginDirectory) => PluginSignatureStatus.NotSigned;
}

public enum PluginTrustState
{
    /// <summary>The user trusted this exact content.</summary>
    Trusted,

    /// <summary>The user has never trusted this plugin.</summary>
    Untrusted,

    /// <summary>The user trusted an earlier version; the files have changed since.</summary>
    Changed,
}

/// <summary>What the user has decided about plugins: which are enabled, and which content they trust.</summary>
public interface IPluginPolicy
{
    bool IsEnabled(string pluginId);
    PluginTrustState GetTrust(string pluginId, string contentHash);
}

/// <summary>A policy the user can change (Options page, CLI).</summary>
public interface IPluginPolicyStore : IPluginPolicy
{
    /// <summary>Remember that the user chose "don't ask again" for this exact content (a changed plugin asks again).</summary>
    void DismissPrompt(string pluginId, string contentHash);

    /// <summary>True if <see cref="DismissPrompt"/> was called for this plugin and exact content.</summary>
    bool IsPromptDismissed(string pluginId, string contentHash);

    /// <summary>Trust exactly this content for the plugin; any previous trust is replaced.</summary>
    void Trust(string pluginId, string contentHash);

    void Revoke(string pluginId);
    void SetEnabled(string pluginId, bool enabled);
}

/// <summary>SHA-256 over every file in a plugin folder, so any change to a plugin's code or manifest changes the hash.</summary>
public static class PluginHasher
{
    public static string Compute(string pluginDirectory)
    {
        var root = Path.GetFullPath(pluginDirectory);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => (Path: f, Relative: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .OrderBy(f => f.Relative, StringComparer.Ordinal);

        var buffer = new byte[81920];
        foreach (var (path, relative) in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData([0]);
            using var stream = File.OpenRead(path);
            hash.AppendData(BitConverter.GetBytes(stream.Length));
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer.AsSpan(0, read));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

/// <summary>
/// Policy persisted as JSON next to the app's other settings. Reads tolerate a missing or corrupt file (treated as
/// "nothing trusted, everything enabled"); writes go through a temp file so a crash cannot leave half a file.
/// </summary>
public sealed class FilePluginPolicyStore : IPluginPolicyStore
{
    private sealed class Data
    {
        public List<string> Disabled { get; set; } = [];
        public Dictionary<string, string> Trusted { get; set; } = [];
        public Dictionary<string, string> Dismissed { get; set; } = [];
    }

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;
    private readonly Lock _lock = new();
    private Data _data;

    public FilePluginPolicyStore(string path)
    {
        _path = path;
        _data = Read(path);
    }

    /// <summary>Per-user policy file: <c>Documents/Toucan/plugin-policy.json</c>.</summary>
    public static string DefaultPath() => Path.Combine(
        Toucan.Core.Services.UserDataFolder.Root, "Toucan", "plugin-policy.json");

    public bool IsEnabled(string pluginId)
    {
        lock (_lock) return !_data.Disabled.Contains(pluginId, StringComparer.OrdinalIgnoreCase);
    }

    public PluginTrustState GetTrust(string pluginId, string contentHash)
    {
        lock (_lock)
        {
            var trusted = _data.Trusted.FirstOrDefault(kv => string.Equals(kv.Key, pluginId, StringComparison.OrdinalIgnoreCase));
            if (trusted.Key is null) return PluginTrustState.Untrusted;
            return string.Equals(trusted.Value, contentHash, StringComparison.OrdinalIgnoreCase)
                ? PluginTrustState.Trusted
                : PluginTrustState.Changed;
        }
    }

    public void Trust(string pluginId, string contentHash)
    {
        lock (_lock)
        {
            Remove(_data.Trusted, pluginId);
            Remove(_data.Dismissed, pluginId);
            _data.Trusted[pluginId] = contentHash;
            Write();
        }
    }

    public void Revoke(string pluginId)
    {
        lock (_lock)
        {
            Remove(_data.Trusted, pluginId);
            Write();
        }
    }

    public void DismissPrompt(string pluginId, string contentHash)
    {
        lock (_lock)
        {
            Remove(_data.Dismissed, pluginId);
            _data.Dismissed[pluginId] = contentHash;
            Write();
        }
    }

    public bool IsPromptDismissed(string pluginId, string contentHash)
    {
        lock (_lock)
            return _data.Dismissed.Any(kv => string.Equals(kv.Key, pluginId, StringComparison.OrdinalIgnoreCase)
                                             && string.Equals(kv.Value, contentHash, StringComparison.OrdinalIgnoreCase));
    }

    public void SetEnabled(string pluginId, bool enabled)
    {
        lock (_lock)
        {
            _data.Disabled.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (!enabled) _data.Disabled.Add(pluginId);
            Write();
        }
    }

    private static void Remove(Dictionary<string, string> map, string id)
    {
        foreach (var key in map.Keys.Where(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase)).ToList())
            map.Remove(key);
    }

    private static Data Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Data();
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(path), s_options) ?? new Data();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Data();
        }
    }

    private void Write()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, s_options));
        File.Move(temp, _path, overwrite: true);
    }
}
