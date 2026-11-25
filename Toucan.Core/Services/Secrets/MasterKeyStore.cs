using System.Security.Cryptography;
using System.Text;

namespace Toucan.Core.Services.Secrets;

public enum MasterKeyStatus
{
    /// <summary>The store holds a key.</summary>
    Found,

    /// <summary>The store works and holds no key yet.</summary>
    NotFound,

    /// <summary>The store cannot be used right now: not installed, locked, no session, or access refused.</summary>
    Unavailable,
}

/// <summary>The result of reading the key that encrypts saved secrets.</summary>
public readonly record struct MasterKeyLookup(MasterKeyStatus Status, ReadOnlyMemory<byte> Key = default, string? Error = null)
{
    public static MasterKeyLookup NotFound => new(MasterKeyStatus.NotFound);
    public static MasterKeyLookup Unavailable(string error) => new(MasterKeyStatus.Unavailable, Error: error);
    public static MasterKeyLookup Found(byte[] key) => new(MasterKeyStatus.Found, key);
}

/// <summary>
/// Where the key that encrypts <c>secrets.json</c> lives when the operating system offers a credential store (macOS
/// Keychain, the freedesktop Secret Service on Linux). Keeping it there means a copy of the Toucan folder alone cannot
/// decrypt the secrets. Windows encrypts each value with DPAPI instead and needs no key.
/// </summary>
public interface IMasterKeyStore
{
    /// <summary>Shown to users and written to the marker file, e.g. "macOS Keychain".</summary>
    string Name { get; }

    MasterKeyLookup Read();

    /// <summary>Stores <paramref name="key"/>, replacing any earlier one. Returns false with a reason when it could not.</summary>
    bool Write(byte[] key, out string? failure);
}

/// <summary>No operating-system store: the key stays in a file with owner-only permissions.</summary>
public sealed class NoMasterKeyStore : IMasterKeyStore
{
    public static NoMasterKeyStore Instance { get; } = new();

    public string Name => "key file";

    public MasterKeyLookup Read() => MasterKeyLookup.Unavailable("No system credential store is used.");

    public bool Write(byte[] key, out string? failure)
    {
        failure = "No system credential store is used.";
        return false;
    }
}

public static class MasterKeyStores
{
    /// <summary>Set to <c>file</c> to keep the key in a file even where a system store exists (for development builds).</summary>
    public const string EnvironmentVariable = "TOUCAN_SECRET_KEY_STORE";

    /// <summary>The store for this system and the key file at <paramref name="keyFile"/>, honouring <see cref="EnvironmentVariable"/>.</summary>
    public static IMasterKeyStore ForCurrentPlatform(string keyFile)
    {
        if (string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "file", StringComparison.OrdinalIgnoreCase))
            return NoMasterKeyStore.Instance;
        var account = AccountFor(keyFile);
        if (OperatingSystem.IsMacOS()) return new MacKeychainMasterKeyStore(account);
        if (OperatingSystem.IsLinux()) return new SecretServiceMasterKeyStore(account);
        return NoMasterKeyStore.Instance;
    }

    /// <summary>One store entry per key-file location, so a separate data folder (tests, portable installs) gets its own key.</summary>
    public static string AccountFor(string keyFile)
    {
        var full = Path.GetFullPath(keyFile);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..16].ToLowerInvariant();
        return $"secret-key-{hash}";
    }
}

/// <summary>
/// The key that encrypts saved secrets is in a system store that cannot be read now. Saving a secret fails with this
/// instead of encrypting it with a new key; reading one returns nothing.
/// </summary>
public sealed class SecretKeyUnavailableException : InvalidOperationException
{
    public SecretKeyUnavailableException() { }
    public SecretKeyUnavailableException(string message) : base(message) { }
    public SecretKeyUnavailableException(string message, Exception inner) : base(message, inner) { }
}
