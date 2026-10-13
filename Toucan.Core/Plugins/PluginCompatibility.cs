using System.Reflection;
using Toucan.Plugins;

namespace Toucan.Core.Plugins;

/// <summary>
/// Decides whether this host can run a plugin, before any of its code loads. Each answer says what is wrong and what to do,
/// because the plugin author and the user read the same text on the Plugins page.
/// </summary>
public static class PluginCompatibility
{
    /// <summary>The first reason the plugin cannot load here, or null when it can.</summary>
    public static string? Check(PluginManifest manifest, Version hostVersion, string platform)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(hostVersion);

        var api = manifest.ParsedApiVersion!;
        if (!PluginApi.IsCompatible(api))
        {
            return api.Major != PluginApi.Current.Major
                ? $"Built for plugin API {api}, but this Toucan implements {PluginApi.Current}. Ask the author for a build for plugin API {PluginApi.Current.Major}.x."
                : $"Built for plugin API {api}, which is newer than the {PluginApi.Current} this Toucan implements. Update Toucan, or use an older version of the plugin.";
        }

        if (manifest.ParsedMinHostVersion is { } min && Normalize(hostVersion) < Normalize(min))
            return $"Needs Toucan {min} or newer, but this is {Normalize(hostVersion)}. Update Toucan, or use an older version of the plugin.";

        if (manifest.Platforms is { Count: > 0 } platforms && !platforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
        {
            var here = platform.Length > 0 ? platform : "this operating system";
            return $"Supports {string.Join(", ", platforms)} only, and this is {here}. Use a build of the plugin for {here}.";
        }

        return null;
    }

    /// <summary>The version of this Core assembly, which is the version of Toucan.</summary>
    public static Version DefaultHostVersion() =>
        typeof(PluginCompatibility).Assembly.GetName().Version ?? new Version(0, 0);

    // Assembly versions carry a fourth part and a manifest usually has three; compare major.minor.build only.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
