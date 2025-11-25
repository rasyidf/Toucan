using System;
using System.Text;
using System.Security.Cryptography;

namespace Toucan.Services;

public class SecureStorageService : ISecureStorageService
{
    // Use current-user DPAPI protected data. This is Windows DPAPI and will work on Windows.
    // If the platform doesn't support ProtectedData, fall back to base64 (not secure) and log in future enhancements.

    public string Protect(string plain)
    {
        if (plain == null) return string.Empty;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var enc = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }
        catch
        {
            // fallback to weak protection if DPAPI not available
            var fallback = Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            return fallback;
        }
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return string.Empty;

        try
        {
            var enc = Convert.FromBase64String(protectedValue);
            var bytes = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // fallback to base64 decode
            try
            {
                var bytes = Convert.FromBase64String(protectedValue);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
