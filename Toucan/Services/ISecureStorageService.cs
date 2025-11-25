namespace Toucan.Services;

public interface ISecureStorageService
{
    /// <summary>
    /// Protect a string and return an opaque payload safe to write to disk.
    /// </summary>
    /// <param name="plain">Plain text</param>
    /// <returns>Base64-encoded protected value</returns>
    string Protect(string plain);

    /// <summary>
    /// Unprotect a previously protected value.
    /// </summary>
    /// <param name="protectedValue">Base64 protected value</param>
    /// <returns>Decrypted plain text</returns>
    string Unprotect(string protectedValue);
}
