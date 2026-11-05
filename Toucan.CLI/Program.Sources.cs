using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;

namespace Toucan.CLI;

internal static class SourcesExit
{
    public const int SignInRequired = 3;
}

/// <summary><c>toucan sources …</c>: connections, accounts, sign-in and connection tests.</summary>
internal static partial class Program
{
    private static int RunSources(string[] args)
    {
        var positional = args.Skip(1).Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && !IsOptionValue(args, i + 1)).ToList();
        var sub = positional.Count > 0 ? positional[0].ToLowerInvariant() : "list";
        string Folder(int index) => Path.GetFullPath(positional.Count > index ? positional[index] : Directory.GetCurrentDirectory());

        return sub switch
        {
            "list" => ListConnections(Folder(1)),
            "adapters" => ListAdapters(),
            "accounts" => positional.Count > 2 && positional[1] == "remove" ? SignOut(positional[2]) : ListAccounts(),
            "login" when positional.Count > 1 => Login(positional[1], Folder(2), args),
            "test" when positional.Count > 1 => TestConnection(positional[1], Folder(2)),
            "login" or "test" => Error($"Usage: toucan sources {sub} <connection> [folder]"),
            _ => Error($"Unknown sources command: {sub}. Use list, adapters, accounts, login or test."),
        };
    }

    private static readonly string[] s_valueOptions = ["--method", "--user", "--name", "--allow-plugin"];

    private static bool IsOptionValue(string[] args, int index) => index > 0 && s_valueOptions.Contains(args[index - 1], StringComparer.Ordinal);

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static ProjectSettings LoadProjectSettings(string folder) =>
        ProjectSettings.LoadFrom(folder) ?? throw new InvalidOperationException($"{folder} has no toucan.tproj.");

    private static SourceConnectionSettings FindConnection(ProjectSettings settings, string connectionId) =>
        settings.Sources.Find(c => c.Id == connectionId)
        ?? throw new InvalidOperationException($"The project has no connection '{connectionId}'. Run 'toucan sources list'.");

    private static int ListAdapters()
    {
        var adapters = Get<ISourceAdapterCatalog>().Adapters;
        if (adapters.Count == 0) { Console.WriteLine("No content-source adapters are loaded. Install and trust a plugin that provides one."); return 0; }
        foreach (var a in adapters)
        {
            var d = a.Descriptor;
            Console.WriteLine($"{d.Id}  {d.DisplayName}{(a.IsBuiltIn ? " (built in)" : $" (plugin {a.PluginId})")}");
            Console.WriteLine($"  address:  {d.DefaultBaseUrl?.ToString() ?? "(you enter one)"}{(d.AllowsCustomBaseUrl ? ", custom addresses allowed" : string.Empty)}");
            Console.WriteLine($"  sign-in:  {string.Join(", ", d.AuthMethods.Select(m => $"{m.Id} ({Describe(m)})"))}");
            Console.WriteLine($"  supports: {d.Capabilities}");
        }
        return 0;
    }

    private static string Describe(SourceAuthMethod method) => method switch
    {
        ApiTokenAuth => "token",
        BasicAuth => "user and password",
        OAuthAuthorizationCodeAuth => "browser, desktop app only",
        OAuthDeviceCodeAuth => "device code",
        CustomAuth => "form, desktop app only",
        _ => method.GetType().Name,
    };

    private static int ListAccounts()
    {
        var accounts = Get<ISourceAccountStore>().Accounts;
        if (accounts.Count == 0) { Console.WriteLine("No accounts. Sign in with 'toucan sources login <connection>'."); return 0; }
        var broker = Get<SourceCredentialBroker>();
        foreach (var a in accounts)
        {
            var credential = broker.Read(a);
            var state = credential is null ? "not signed in" : credential.ExpiresAt is { } at ? $"signed in, token until {at:u}" : "signed in";
            Console.WriteLine($"{a.Id}  {a.DisplayName}");
            Console.WriteLine($"  {a.Adapter} at {a.BaseUrl} via {a.AuthMethod}: {state}");
        }
        return 0;
    }

    private static int SignOut(string accountId)
    {
        var removed = Get<SourceSignInService>().SignOutAsync(accountId).GetAwaiter().GetResult();
        Console.WriteLine(removed ? $"Signed out and removed {accountId}." : $"No account {accountId}.");
        return removed ? 0 : 1;
    }

    private static int ListConnections(string folder)
    {
        var settings = LoadProjectSettings(folder);
        if (settings.Sources.Count == 0) { Console.WriteLine("The project has no content-source connections."); return 0; }
        // Only works out owners and accounts: nothing is opened and no plugin code runs.
        var service = new SourceConnectionService(null, Get<ISourceAdapterCatalog>(), Get<SourceSignInService>()) { OpensConnections = false };
        service.OpenWorkspaceAsync(folder, [.. settings.Sources.Select(c => c.Clone())]).GetAwaiter().GetResult();
        foreach (var c in settings.Sources)
        {
            var status = service.Status(c.Id);
            var owner = c.Adapter is { Length: > 0 } adapter ? $"adapter {adapter}" : c.Plugin is { Length: > 0 } plugin ? $"plugin {plugin}" : "no owner";
            Console.WriteLine($"{c.Id}  {c.Name ?? c.RemoteProject?.Name ?? string.Empty}".TrimEnd());
            Console.WriteLine($"  {owner}{(c.RemoteProject is { } p ? $", remote project {p.Id}" : string.Empty)}, {c.Role}");
            Console.WriteLine($"  languages: {(c.Languages.Count == 0 ? "none mapped" : string.Join(", ", c.Languages.Select(l => $"{l.Key}→{l.Value}")))}");
            Console.WriteLine($"  state:     {status?.Summary ?? "unknown"}");
        }
        return 0;
    }

    private static int Login(string connectionId, string folder, string[] args)
    {
        var settings = LoadProjectSettings(folder);
        var connection = FindConnection(settings, connectionId);
        if (string.IsNullOrWhiteSpace(connection.Adapter))
            return Error($"Connection {connectionId} is managed by plugin {connection.Plugin ?? "(none)"} and has no sign-in here.");
        var signIn = Get<SourceSignInService>();
        var adapter = signIn.Adapter(connection.Adapter);
        var methods = adapter.Descriptor.AuthMethods;
        var name = Option(args, "--name");
        var tokenFromStdin = args.Contains("--token-stdin", StringComparer.Ordinal);
        var user = Option(args, "--user");
        var method = Option(args, "--method") is { } id ? signIn.Method(adapter, id)
            : tokenFromStdin ? methods.OfType<ApiTokenAuth>().FirstOrDefault() ?? throw new InvalidOperationException($"{adapter.Descriptor.DisplayName} has no token sign-in.")
            : user is not null ? methods.OfType<BasicAuth>().FirstOrDefault() ?? throw new InvalidOperationException($"{adapter.Descriptor.DisplayName} has no password sign-in.")
            : methods.OfType<OAuthDeviceCodeAuth>().FirstOrDefault() ?? (SourceAuthMethod?)methods.OfType<ApiTokenAuth>().FirstOrDefault()
              ?? throw new InvalidOperationException($"{adapter.Descriptor.DisplayName} can only be signed in to from the desktop app.");

        SourceAccount account = method switch
        {
            OAuthDeviceCodeAuth => signIn.SignInWithDeviceCodeAsync(adapter.Descriptor.Id, connection.BaseUrl, method.Id, prompt =>
            {
                Console.Error.WriteLine($"Open {prompt.VerificationUriComplete ?? prompt.VerificationUri} and enter the code {prompt.UserCode}.");
                Console.Error.WriteLine($"Waiting until {prompt.ExpiresAt.ToLocalTime():t}…");
                return Task.CompletedTask;
            }, name).GetAwaiter().GetResult(),
            ApiTokenAuth => signIn.SignInWithTokenAsync(adapter.Descriptor.Id, connection.BaseUrl, method.Id,
                ReadSecretFromStdin(tokenFromStdin, "token"), name).GetAwaiter().GetResult(),
            BasicAuth => signIn.SignInWithPasswordAsync(adapter.Descriptor.Id, connection.BaseUrl, method.Id,
                user ?? throw new InvalidOperationException("Pass --user <name>; the password is read from standard input."),
                ReadSecretFromStdin(true, "password"), name).GetAwaiter().GetResult(),
            _ => throw new InvalidOperationException($"Sign-in method '{method.Id}' ({Describe(method)}) is not available in the CLI."),
        };
        Get<ISourceAccountStore>().Bind(folder, connectionId, account.Id);
        Console.WriteLine($"Signed in as {account.DisplayName} ({account.Id}); connection {connectionId} uses this account.");
        return 0;
    }

    private static string ReadSecretFromStdin(bool requested, string what)
    {
        if (!requested && !Console.IsInputRedirected)
            throw new InvalidOperationException($"Pass --token-stdin and pipe the {what} in, so it does not appear in your shell history.");
        var value = Console.In.ReadLine()?.Trim();
        return string.IsNullOrEmpty(value) ? throw new InvalidOperationException($"No {what} on standard input.") : value;
    }

    private static int TestConnection(string connectionId, string folder)
    {
        var settings = LoadProjectSettings(folder);
        FindConnection(settings, connectionId);
        var activation = Get<IPluginActivationService>();
        var service = new SourceConnectionService(activation, Get<ISourceAdapterCatalog>(), Get<SourceSignInService>());
        activation.OpenWorkspaceAsync(folder).GetAwaiter().GetResult();
        try
        {
            service.OpenWorkspaceAsync(folder, settings.Sources).GetAwaiter().GetResult();
            var result = service.TestAsync(connectionId).GetAwaiter().GetResult();
            var status = service.Status(connectionId);
            Console.WriteLine(result.Succeeded ? $"OK: {result.Message}" : $"Failed: {result.Message}");
            return result.Succeeded ? 0 : status?.State == SourceConnectionState.SignInRequired || result.Error?.Kind == SourceErrorKind.Authentication ? SourcesExit.SignInRequired : 1;
        }
        finally
        {
            service.CloseWorkspaceAsync().GetAwaiter().GetResult();
            activation.CloseWorkspaceAsync(folder).GetAwaiter().GetResult();
        }
    }
}
