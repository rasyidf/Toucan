using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Toucan.Core.Contracts;

namespace Toucan.Core.Services;

/// <summary>
/// Encrypts secrets (API keys) at rest; <see cref="SecretService"/> stores them.
/// Windows uses DPAPI (same format the WPF app used, so older settings files still decrypt).
/// macOS and Linux use AES-GCM with a per-user random key stored in the user's config
/// directory with owner-only (0600) permissions.
/// </summary>
public sealed class SecureStorageService : ISecureStorageService
{
    private const string AesPrefix = "aesgcm1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly string _keyFile;
    private byte[]? _key;

    public SecureStorageService()
        : this(Path.Combine(DefaultFolder, "secret.key"))
    {
    }

    /// <param name="keyFile">Where the AES key lives on macOS and Linux (created on first use).</param>
    public SecureStorageService(string keyFile) => _keyFile = keyFile;

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
            catch (Exception ex) when (ex is CryptographicException or FormatException) { return string.Empty; }
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
        if (_key != null) return _key;

        if (File.Exists(_keyFile))
        {
            var existing = File.ReadAllBytes(_keyFile);
            if (existing.Length == 32) return _key = existing;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_keyFile)!);
        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(_keyFile, key);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return _key = key;
    }
}
