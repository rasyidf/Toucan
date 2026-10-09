using System.Security.Cryptography;
using System.Text;

namespace Toucan.Core.Contracts;

/// <summary>
/// The one place Toucan keeps secrets (API keys, tokens). Values are encrypted at rest and live in the user's
/// application-data folder, never in a project folder, so they cannot end up in version control.
/// Keys are paths such as <c>ai/anthropic/api_key</c>; see <see cref="SecretKeys"/>.
/// </summary>
public interface ISecretService
{
    /// <summary>The secret, or null when none is stored (or it can no longer be decrypted).</summary>
    string? GetSecret(string key);

    /// <summary>Stores <paramref name="value"/>; null or empty removes the secret.</summary>
    void SetSecret(string key, string? value);

    bool Remove(string key);

    /// <summary>Stored keys starting with <paramref name="prefix"/> (all when empty), sorted. Values are not returned.</summary>
    IReadOnlyList<string> Keys(string prefix = "");

    /// <summary>Removes every secret whose key starts with <paramref name="prefix"/>; returns how many.</summary>
    int RemoveAll(string prefix);
}

/// <summary>Builds secret keys, so every caller names the same secret the same way. Keys are lowercase.</summary>
public static class SecretKeys
{
    public const string ApiKey = "api_key";

    /// <summary><c>ai/&lt;service&gt;/&lt;field&gt;</c>: an AI service's credentials, e.g. <c>ai/anthropic/api_key</c>.</summary>
    public static string Ai(string backendId, string field = ApiKey) => $"ai/{Normalize(backendId)}/{Normalize(field)}";

    /// <summary>
    /// Where a translation provider's secrets live: <c>mt/</c> app-wide, or <c>project/&lt;id&gt;/mt/</c> for one project's
    /// override, where the id is a hash of the project folder so the folder path itself is not stored.
    /// </summary>
    public static string ProviderScope(string? projectPath) =>
        string.IsNullOrEmpty(projectPath) ? "mt/" : $"project/{ProjectId(projectPath)}/mt/";

    /// <summary><c>mt/&lt;provider&gt;/&lt;field&gt;</c> (or the project-scoped form), e.g. <c>mt/deepl/api_key</c>.</summary>
    public static string Provider(string provider, string field, string? projectPath = null) =>
        $"{ProviderScope(projectPath)}{Normalize(provider)}/{Normalize(field)}";

    /// <summary><c>plugin/&lt;plugin&gt;/&lt;scope&gt;/&lt;name&gt;</c>: a plugin's secret. The scope is <c>app</c>, <c>ws-&lt;project id&gt;</c> or <c>conn-&lt;id&gt;</c>.</summary>
    public static string Plugin(string pluginId, string scope, string name) => $"plugin/{Normalize(pluginId)}/{scope}/{Normalize(name)}";

    /// <summary>A short stable id for a project folder: the first 16 hex digits of the SHA-256 of its full path.</summary>
    public static string ProjectId(string projectPath)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectPath));
        // Windows and macOS folders are case-insensitive by default; the same folder must give the same id.
        if (!OperatingSystem.IsLinux()) full = full.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16].ToLowerInvariant();
    }

    private static string Normalize(string part) => part.Trim().Replace('/', '_').ToLowerInvariant();
}
