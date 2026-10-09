using System.Text.Json;
using Toucan.Core.Services;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>A plugin's own writable folder under <c>Documents/Toucan/plugin-data/&lt;plugin id&gt;</c>.</summary>
internal sealed class PluginStorage : IPluginStorage
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root;

    public PluginStorage(string dataRoot, string pluginId)
    {
        // The plugin ID is validated by the manifest (lowercase letters, digits, '.', '-'), so it is a safe folder name.
        _root = Path.GetFullPath(Path.Combine(dataRoot, pluginId));
    }

    public string DataDirectory
    {
        get
        {
            Directory.CreateDirectory(_root);
            return _root;
        }
    }

    public string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("The path must be relative to the plugin's data folder.", nameof(relativePath));

        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        var rooted = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rooted, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The path would leave the plugin's data folder.", nameof(relativePath));
        return full;
    }

    public bool Exists(string relativePath) => File.Exists(ResolvePath(relativePath));

    public async Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(relativePath);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task WriteTextAsync(string relativePath, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await AtomicFile.WriteAllTextAsync(path, text).ConfigureAwait(false);
    }

    public async Task<T?> ReadJsonAsync<T>(string relativePath, CancellationToken cancellationToken = default)
    {
        var text = await ReadTextAsync(relativePath, cancellationToken).ConfigureAwait(false);
        return text is null ? default : JsonSerializer.Deserialize<T>(text, s_json);
    }

    public Task WriteJsonAsync<T>(string relativePath, T value, CancellationToken cancellationToken = default) =>
        WriteTextAsync(relativePath, JsonSerializer.Serialize(value, s_json), cancellationToken);

    public bool Delete(string relativePath)
    {
        var path = ResolvePath(relativePath);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}
