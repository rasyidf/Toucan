using System.Diagnostics;
using System.Runtime.Versioning;

namespace Toucan.Core.Services.Secrets;

/// <summary>
/// Keeps the secrets key in the freedesktop Secret Service (GNOME Keyring, KWallet) through <c>secret-tool</c> from libsecret.
/// The key is passed on standard input, never on the command line. Without a session bus or <c>secret-tool</c> the store
/// reports itself unavailable and Toucan keeps the key in a file, as before.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class SecretServiceMasterKeyStore(string account, string tool = "secret-tool", TimeSpan? timeout = null) : IMasterKeyStore
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(30);

    public string Name => "Secret Service";

    public MasterKeyLookup Read()
    {
        if (Unusable() is { } reason) return MasterKeyLookup.Unavailable(reason);
        var (exit, output, error) = Run(["lookup", "application", "toucan", "account", account], stdin: null);
        if (exit is null) return MasterKeyLookup.Unavailable(error);
        // secret-tool exits 1 with nothing on stderr when there is no such item.
        if (exit != 0) return error.Length == 0 ? MasterKeyLookup.NotFound : MasterKeyLookup.Unavailable(error);
        try
        {
            return MasterKeyLookup.Found(Convert.FromBase64String(output.Trim()));
        }
        catch (FormatException)
        {
            return MasterKeyLookup.Unavailable("The stored Toucan key is not in the expected format.");
        }
    }

    public bool Write(byte[] key, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Unusable() is { } reason) { failure = reason; return false; }
        var (exit, _, stderr) = Run(["store", "--label=Toucan secret key", "application", "toucan", "account", account], Convert.ToBase64String(key));
        failure = exit == 0 ? null : stderr.Length > 0 ? stderr : $"secret-tool exited with {exit}";
        return exit == 0;
    }

    private static string? Unusable() =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
            ? "No desktop session bus, so the Secret Service cannot be reached."
            : null;

    private (int? Exit, string Output, string Error) Run(IReadOnlyList<string> arguments, string? stdin)
    {
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return (null, string.Empty, "secret-tool could not be started.");
            if (stdin is not null) process.StandardInput.Write(stdin);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(_timeout))
            {
                process.Kill(entireProcessTree: true);
                return (null, string.Empty, "The Secret Service did not answer in time (is the keyring waiting to be unlocked?).");
            }
            return (process.ExitCode, output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult().Trim());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (null, string.Empty, "secret-tool is not installed (package libsecret-tools or libsecret).");
        }
    }
}
