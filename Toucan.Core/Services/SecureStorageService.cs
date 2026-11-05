using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Toucan.Core.Contracts;
using Toucan.Core.Services.Secrets;

namespace Toucan.Core.Services;

/// <summary>
/// Encrypts secrets (API keys) at rest; <see cref="SecretService"/> stores them.
/// Windows uses DPAPI (same format the WPF app used, so older settings files still decrypt).
/// macOS and Linux use AES-GCM with a per-user random key. The key lives in the system credential store (macOS
/// Keychain, the Linux Secret Service) when there is one, and otherwise in a file in the user's config directory with
/// owner-only (0600) permissions. An existing key file moves into the system store on first use.
/// </summary>
/// <remarks>
/// Once the key has moved, a marker file next to where the key file was records the store's name. From then on the key is
/// only taken from that store: if it cannot be read (a locked Keychain, an SSH session), secrets are unavailable rather
/// than encrypted with a new key that would make the saved ones unreadable.
/// </remarks>
public sealed class SecureStorageService : ISecureStorageService
{
    private const string AesPrefix = "aesgcm1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly string _keyFile;
    private readonly IMasterKeyStore _keyStore;
    private readonly Lock _keyLock = new();
    private byte[]? _key;

    /// <summary>The default folder, with the system credential store for this platform.</summary>
    public SecureStorageService()
        : this(Path.Combine(DefaultFolder, "secret.key"), MasterKeyStores.ForCurrentPlatform(Path.Combine(DefaultFolder, "secret.key")))
    {
    }

    /// <param name="keyFile">Where the AES key lives on macOS and Linux (created on first use). No system store is used.</param>
    public SecureStorageService(string keyFile) : this(keyFile, NoMasterKeyStore.Instance) { }

    /// <param name="keyFile">The key file: read when it exists, moved into <paramref name="keyStore"/> when that works.</param>
    /// <param name="keyStore">The system credential store, or <see cref="NoMasterKeyStore"/>.</param>
    public SecureStorageService(string keyFile, IMasterKeyStore keyStore)
    {
        _keyFile = keyFile;
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
    }

    /// <summary>The marker that says the key moved into a system store; it holds the store's name.</summary>
    public string MarkerFile => _keyFile + ".store";

    /// <summary>Where the key is kept now: the system store's name, "key file", or null before first use.</summary>
    public string? KeyLocation { get; private set; }

    /// <summary>The per-user folder holding the key and the secret store: <c>Toucan</c> under the application-data folder.</summary>
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Toucan");

    public string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;
        return OperatingSystem.IsWindows() ? ProtectDpapi(plain) : ProtectAes(plain);
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return string.Empty;
        if (protectedValue.StartsWith(AesPrefix, StringComparison.Ordinal))
        {
            try { return UnprotectAes(protectedValue[AesPrefix.Length..]); }
            catch (Exception ex) when (ex is CryptographicException or FormatException or SecretKeyUnavailableException) { return string.Empty; }
        }

        if (OperatingSystem.IsWindows())
        {
            // Not DPAPI data (for example plain base64 from the old WPF app): fall through to the legacy reader.
            try { return UnprotectDpapi(protectedValue); }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { }
        }

        // Legacy fallback: the WPF app stored plain base64 when DPAPI was unavailable.
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue)); }
        catch (FormatException) { return string.Empty; }
    }

    [SupportedOSPlatform("windows")]
    private static string ProtectDpapi(string plain)
    {
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    [SupportedOSPlatform("windows")]
    private static string UnprotectDpapi(string value)
    {
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private string ProtectAes(string plain)
    {
        var key = GetOrCreateKey();
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var payload = new byte[NonceSize + TagSize + plainBytes.Length];
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        return AesPrefix + Convert.ToBase64String(payload);
    }

    private string UnprotectAes(string value)
    {
        var payload = Convert.FromBase64String(value);
        if (payload.Length < NonceSize + TagSize) return string.Empty;
        var key = GetOrCreateKey();
        var plain = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] GetOrCreateKey()
    {
        lock (_keyLock)
        {
            return _key ??= LoadKey();
        }
    }

    private byte[] LoadKey()
    {
        // The key moved into a system store earlier: only that store may supply it.
        if (ReadMarker() is { } storeName)
        {
            if (!string.Equals(storeName, _keyStore.Name, StringComparison.Ordinal))
                throw new SecretKeyUnavailableException($"Saved secrets are encrypted with a key kept in the {storeName}, which this Toucan does not use.");
            var stored = _keyStore.Read();
            if (stored is { Status: MasterKeyStatus.Found, Key.Length: KeySize })
            {
                KeyLocation = _keyStore.Name;
                return stored.Key.ToArray();
            }
            throw new SecretKeyUnavailableException(stored.Status == MasterKeyStatus.Found
                ? $"The key in the {storeName} is not a Toucan key."
                : $"The key for saved secrets is in the {storeName} and could not be read: {stored.Error ?? "it is missing"}.");
        }

        var lookup = _keyStore.Read();
        var fileKey = ReadKeyFile();
        if (fileKey is not null)
        {
            // The secrets were written with the file's key, so it wins over anything in the store.
            if (lookup.Status is MasterKeyStatus.NotFound or MasterKeyStatus.Found && TryMoveToStore(fileKey))
                File.Delete(_keyFile);
            KeyLocation = File.Exists(_keyFile) ? NoMasterKeyStore.Instance.Name : _keyStore.Name;
            return fileKey;
        }

        if (lookup is { Status: MasterKeyStatus.Found, Key.Length: KeySize })
        {
            WriteMarker();
            KeyLocation = _keyStore.Name;
            return lookup.Key.ToArray();
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        if (lookup.Status == MasterKeyStatus.NotFound && TryMoveToStore(key))
        {
            KeyLocation = _keyStore.Name;
            return key;
        }

        WriteKeyFile(key);
        KeyLocation = NoMasterKeyStore.Instance.Name;
        return key;
    }

    /// <summary>Stores the key, reads it back to be sure, then writes the marker. Returns false and changes nothing visible otherwise.</summary>
    private bool TryMoveToStore(byte[] key)
    {
        if (!_keyStore.Write(key, out _)) return false;
        var check = _keyStore.Read();
        if (check.Status != MasterKeyStatus.Found || !CryptographicOperations.FixedTimeEquals(check.Key.Span, key)) return false;
        WriteMarker();
        return true;
    }

    private byte[]? ReadKeyFile()
    {
        if (!File.Exists(_keyFile)) return null;
        var existing = File.ReadAllBytes(_keyFile);
        return existing.Length == KeySize ? existing : null;
    }

    private void WriteKeyFile(byte[] key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_keyFile))!);
        File.WriteAllBytes(_keyFile, key);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private string? ReadMarker() => File.Exists(MarkerFile) ? File.ReadAllText(MarkerFile).Trim() is { Length: > 0 } name ? name : null : null;

    private void WriteMarker()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(MarkerFile))!);
        var temp = MarkerFile + ".tmp";
        File.WriteAllText(temp, _keyStore.Name);
        File.Move(temp, MarkerFile, overwrite: true);
    }
}
