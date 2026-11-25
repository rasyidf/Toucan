using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>One line describing a manifest's <c>network</c> block, shown wherever the user decides to trust a plugin.</summary>
public static class PluginNetworkText
{
    public static string Describe(PluginManifest? manifest)
    {
        if (manifest?.Network is not { } network) return string.Empty;
        var hosts = network.Hosts ?? [];
        var listed = hosts.Count > 0 ? string.Join(", ", hosts) : string.Empty;
        return (listed.Length > 0, network.UserDefinedHosts) switch
        {
            (true, true) => $"{listed}, and servers you enter",
            (true, false) => listed,
            (false, true) => "servers you enter",
            _ => string.Empty,
        };
    }
}
