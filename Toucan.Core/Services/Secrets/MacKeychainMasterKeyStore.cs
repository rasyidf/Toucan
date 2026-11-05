using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Toucan.Core.Services.Secrets;

/// <summary>
/// Keeps the secrets key as a generic password in the user's login Keychain (service "Toucan"). The key never appears on
/// a command line: it goes through Security.framework directly.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainMasterKeyStore(string account) : IMasterKeyStore
{
    public const string Service = "Toucan";

    public string Name => "macOS Keychain";

    public MasterKeyLookup Read()
    {
        try
        {
            using var query = Query(returnData: true);
            var status = Native.SecItemCopyMatching(query.Handle, out var result);
            if (status == Native.ErrSecItemNotFound) return MasterKeyLookup.NotFound;
            if (status != 0) return MasterKeyLookup.Unavailable(Describe(status));
            try
            {
                var length = (int)Native.CFDataGetLength(result);
                var bytes = new byte[length];
                Marshal.Copy(Native.CFDataGetBytePtr(result), bytes, 0, length);
                return MasterKeyLookup.Found(bytes);
            }
            finally
            {
                Native.CFRelease(result);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return MasterKeyLookup.Unavailable($"Security.framework is not available: {ex.Message}");
        }
    }

    public bool Write(byte[] key, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(key);
        try
        {
            using var data = CfHandle.Data(key);
            using var query = Query(returnData: false);
            using var update = CfHandle.Dictionary((Native.Constant("kSecValueData"), data.Handle));
            var status = Native.SecItemUpdate(query.Handle, update.Handle);
            if (status == Native.ErrSecItemNotFound)
            {
                using var label = CfHandle.String("Toucan secret key");
                using var serviceName = CfHandle.String(Service);
                using var accountName = CfHandle.String(account);
                using var add = CfHandle.Dictionary(
                    (Native.Constant("kSecClass"), Native.Constant("kSecClassGenericPassword")),
                    (Native.Constant("kSecAttrService"), serviceName.Handle),
                    (Native.Constant("kSecAttrAccount"), accountName.Handle),
                    (Native.Constant("kSecAttrLabel"), label.Handle),
                    (Native.Constant("kSecValueData"), data.Handle));
                status = Native.SecItemAdd(add.Handle, IntPtr.Zero);
            }
            failure = status == 0 ? null : Describe(status);
            return status == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            failure = $"Security.framework is not available: {ex.Message}";
            return false;
        }
    }

    /// <summary>Removes the key (tests and "forget all secrets"). Returns true when one was removed.</summary>
    public bool Delete()
    {
        using var query = Query(returnData: false);
        return Native.SecItemDelete(query.Handle) == 0;
    }

    private CfHandle Query(bool returnData)
    {
        using var serviceName = CfHandle.String(Service);
        using var accountName = CfHandle.String(account);
        // The dictionary retains its values, so the strings can be released when this method returns.
        return returnData
            ? CfHandle.Dictionary(
                (Native.Constant("kSecClass"), Native.Constant("kSecClassGenericPassword")),
                (Native.Constant("kSecAttrService"), serviceName.Handle),
                (Native.Constant("kSecAttrAccount"), accountName.Handle),
                (Native.Constant("kSecReturnData"), Native.Constant("kCFBooleanTrue", Native.CoreFoundation)),
                (Native.Constant("kSecMatchLimit"), Native.Constant("kSecMatchLimitOne")))
            : CfHandle.Dictionary(
                (Native.Constant("kSecClass"), Native.Constant("kSecClassGenericPassword")),
                (Native.Constant("kSecAttrService"), serviceName.Handle),
                (Native.Constant("kSecAttrAccount"), accountName.Handle));
    }

    private static string Describe(int status) => status switch
    {
        Native.ErrSecInteractionNotAllowed => "the Keychain is locked or cannot ask for permission in this session (for example over SSH)",
        Native.ErrSecAuthFailed => "the Keychain refused access",
        Native.ErrSecUserCanceled => "access to the Keychain was declined",
        _ => $"Keychain error {status}",
    };

    private sealed class CfHandle : IDisposable
    {
        private CfHandle(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; private set; }

        public static CfHandle String(string value) =>
            new(Native.CFStringCreateWithCString(IntPtr.Zero, value, Native.KCFStringEncodingUtf8));

        public static CfHandle Data(byte[] bytes) => new(Native.CFDataCreate(IntPtr.Zero, bytes, bytes.Length));

        public static CfHandle Dictionary(params (IntPtr Key, IntPtr Value)[] pairs)
        {
            var keys = pairs.Select(p => p.Key).ToArray();
            var values = pairs.Select(p => p.Value).ToArray();
            return new(Native.CFDictionaryCreate(IntPtr.Zero, keys, values, pairs.Length,
                Native.Symbol("kCFTypeDictionaryKeyCallBacks", Native.CoreFoundation),
                Native.Symbol("kCFTypeDictionaryValueCallBacks", Native.CoreFoundation)));
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            Native.CFRelease(Handle);
            Handle = IntPtr.Zero;
        }
    }

    private static class Native
    {
        public const string Security = "/System/Library/Frameworks/Security.framework/Security";
        public const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        public const uint KCFStringEncodingUtf8 = 0x08000100;
        public const int ErrSecItemNotFound = -25300;
        public const int ErrSecInteractionNotAllowed = -25308;
        public const int ErrSecAuthFailed = -25293;
        public const int ErrSecUserCanceled = -128;

        private static readonly Lazy<IntPtr> s_security = new(() => NativeLibrary.Load(Security));
        private static readonly Lazy<IntPtr> s_coreFoundation = new(() => NativeLibrary.Load(CoreFoundation));

        /// <summary>The address of an exported symbol (for structs such as the dictionary callbacks).</summary>
        public static IntPtr Symbol(string name, string library = Security) =>
            NativeLibrary.GetExport(library == Security ? s_security.Value : s_coreFoundation.Value, name);

        /// <summary>The value of an exported constant such as <c>kSecClass</c> (a CFStringRef global).</summary>
        public static IntPtr Constant(string name, string library = Security) => Marshal.ReadIntPtr(Symbol(name, library));

        [DllImport(Security)]
        public static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

        [DllImport(Security)]
        public static extern int SecItemAdd(IntPtr attributes, IntPtr result);

        [DllImport(Security)]
        public static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

        [DllImport(Security)]
        public static extern int SecItemDelete(IntPtr query);

        [DllImport(CoreFoundation, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        public static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);

        [DllImport(CoreFoundation)]
        public static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

        [DllImport(CoreFoundation)]
        public static extern nint CFDataGetLength(IntPtr data);

        [DllImport(CoreFoundation)]
        public static extern IntPtr CFDataGetBytePtr(IntPtr data);

        [DllImport(CoreFoundation)]
        public static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);

        [DllImport(CoreFoundation)]
        public static extern void CFRelease(IntPtr handle);
    }
}
