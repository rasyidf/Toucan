using System;
using System.Security.Cryptography;
using System.Text;

namespace Toucan.Avalonia.Services;

public class SecureStorageService : ISecureStorageService
{
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
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
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
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue)); }
            catch { return string.Empty; }
        }
    }
}
