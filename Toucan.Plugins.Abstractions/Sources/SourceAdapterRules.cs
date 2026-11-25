namespace Toucan.Plugins.Sources;

/// <summary>
/// The rules an adapter's registration must meet. The host refuses a plugin that breaks one; the conformance suite in
/// <c>Toucan.Plugins.Testing</c> runs the same checks, so authors see them before shipping.
/// </summary>
public static class SourceAdapterRules
{
    private const SourceCapabilities UnitWrites = SourceCapabilities.WriteValues | SourceCapabilities.CreateKeys | SourceCapabilities.RenameKeys
        | SourceCapabilities.DeleteKeys | SourceCapabilities.DeleteTranslations;

    /// <summary>Every problem with <paramref name="adapter"/> as registered by the plugin described by <paramref name="manifest"/>; empty when it is fine.</summary>
    public static IReadOnlyList<string> Check(IContentSourceAdapter adapter, PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(manifest);
        var problems = new List<string>();
        var d = adapter.Descriptor;
        if (d is null) return ["The adapter has no descriptor."];

        if (string.IsNullOrWhiteSpace(d.Id) || !IsValidId(d.Id))
            problems.Add($"Adapter ID '{d.Id}' is invalid: use lowercase letters, digits, '.' and '-'.");
        else if (!d.Id.StartsWith(manifest.Id + ".", StringComparison.Ordinal))
            problems.Add($"Adapter ID '{d.Id}' must start with its plugin ID '{manifest.Id}.'.");
        if (string.IsNullOrWhiteSpace(d.DisplayName)) problems.Add($"Adapter '{d.Id}' needs a display name.");
        if (d.ContractVersion < 1 || d.ContractVersion > SourceContract.Version)
            problems.Add($"Adapter '{d.Id}' was written for content-source contract {d.ContractVersion}; this Toucan understands 1 to {SourceContract.Version}.");

        CheckCapabilities(adapter, d, problems);
        CheckAuth(adapter, d, manifest, problems);
        CheckAddresses(d, manifest, problems);
        return problems;
    }

    private static void CheckCapabilities(IContentSourceAdapter adapter, SourceAdapterDescriptor d, List<string> problems)
    {
        var caps = d.Capabilities;
        if (!caps.HasFlag(SourceCapabilities.ReadUnits) && !caps.HasFlag(SourceCapabilities.ReadDocuments))
            problems.Add($"Adapter '{d.Id}' must declare ReadUnits or ReadDocuments.");
        if ((caps & UnitWrites) != 0 && !caps.HasFlag(SourceCapabilities.ReadUnits))
            problems.Add($"Adapter '{d.Id}' declares unit writes without ReadUnits.");
        if (caps.HasFlag(SourceCapabilities.WriteDocuments) && !caps.HasFlag(SourceCapabilities.ReadDocuments))
            problems.Add($"Adapter '{d.Id}' declares WriteDocuments without ReadDocuments.");
        if (caps.HasFlag(SourceCapabilities.DeltaReads) && !caps.HasFlag(SourceCapabilities.ReadUnits))
            problems.Add($"Adapter '{d.Id}' declares DeltaReads without ReadUnits.");
        if (caps.HasFlag(SourceCapabilities.ProjectDiscovery) && adapter is not ISupportsProjectDiscovery)
            problems.Add($"Adapter '{d.Id}' declares ProjectDiscovery but does not implement ISupportsProjectDiscovery.");
    }

    private static void CheckAuth(IContentSourceAdapter adapter, SourceAdapterDescriptor d, PluginManifest manifest, List<string> problems)
    {
        if (d.AuthMethods is not { Count: > 0 })
        {
            problems.Add($"Adapter '{d.Id}' needs at least one sign-in method.");
            return;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in d.AuthMethods)
        {
            if (method is null) { problems.Add($"Adapter '{d.Id}' lists a null sign-in method."); continue; }
            if (string.IsNullOrWhiteSpace(method.Id) || !ids.Add(method.Id))
                problems.Add($"Adapter '{d.Id}' has a sign-in method with an empty or repeated ID '{method.Id}'.");
            if (string.IsNullOrWhiteSpace(method.Label)) problems.Add($"Sign-in method '{method.Id}' of '{d.Id}' needs a label.");
            switch (method)
            {
                case OAuthAuthorizationCodeAuth code:
                    CheckEndpoint(d.Id, method.Id, "authorize", code.AuthorizeUrl, manifest, problems);
                    CheckEndpoint(d.Id, method.Id, "token", code.TokenUrl, manifest, problems);
                    if (code.RevokeUrl is { } revoke) CheckEndpoint(d.Id, method.Id, "revoke", revoke, manifest, problems);
                    if (string.IsNullOrWhiteSpace(code.ClientId)) problems.Add($"Sign-in method '{method.Id}' of '{d.Id}' needs a client ID.");
                    break;
                case OAuthDeviceCodeAuth device:
                    CheckEndpoint(d.Id, method.Id, "device authorization", device.DeviceAuthorizationUrl, manifest, problems);
                    CheckEndpoint(d.Id, method.Id, "token", device.TokenUrl, manifest, problems);
                    if (device.RevokeUrl is { } deviceRevoke) CheckEndpoint(d.Id, method.Id, "revoke", deviceRevoke, manifest, problems);
                    if (string.IsNullOrWhiteSpace(device.ClientId)) problems.Add($"Sign-in method '{method.Id}' of '{d.Id}' needs a client ID.");
                    break;
                case CustomAuth custom:
                    if (adapter is not ISourceAuthExchange)
                        problems.Add($"Sign-in method '{method.Id}' of '{d.Id}' is custom, so the adapter must implement ISourceAuthExchange.");
                    if (custom.Fields?.Fields is not { Count: > 0 })
                        problems.Add($"Custom sign-in method '{method.Id}' of '{d.Id}' needs at least one field.");
                    break;
                case ApiTokenAuth token:
                    if (string.IsNullOrWhiteSpace(token.HeaderName)) problems.Add($"Token sign-in '{method.Id}' of '{d.Id}' needs a header name.");
                    break;
            }
        }
    }

    private static void CheckAddresses(SourceAdapterDescriptor d, PluginManifest manifest, List<string> problems)
    {
        var network = manifest.Network;
        if (d.AllowsCustomBaseUrl && network?.UserDefinedHosts != true)
            problems.Add($"Adapter '{d.Id}' allows custom addresses, so the manifest's 'network' needs \"userDefinedHosts\": true.");
        if (d.DefaultBaseUrl is { } url)
            CheckEndpoint(d.Id, null, "default", url, manifest, problems);
        else if (!d.AllowsCustomBaseUrl)
            problems.Add($"Adapter '{d.Id}' needs a DefaultBaseUrl or must allow custom addresses.");
    }

    private static void CheckEndpoint(string adapterId, string? methodId, string what, Uri? url, PluginManifest manifest, List<string> problems)
    {
        var where = methodId is null ? $"Adapter '{adapterId}'" : $"Sign-in method '{methodId}' of '{adapterId}'";
        if (url is null || !url.IsAbsoluteUri)
        {
            problems.Add($"{where} has no absolute {what} address.");
            return;
        }
        if (!IsSecure(url))
        {
            problems.Add($"{where}: the {what} address {url} must use HTTPS.");
            return;
        }
        if (url.IsLoopback) return;
        if (manifest.Network?.Allows(url.Host) != true)
            problems.Add($"{where}: the {what} address host '{url.Host}' is not in the manifest's 'network' hosts.");
    }

    /// <summary>HTTPS, or HTTP to this machine (local test servers).</summary>
    public static bool IsSecure(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback));
    }

    private static bool IsValidId(string id) =>
        id.Length <= 96 && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-')
        && char.IsLetterOrDigit(id[0]) && char.IsLetterOrDigit(id[^1]);
}
