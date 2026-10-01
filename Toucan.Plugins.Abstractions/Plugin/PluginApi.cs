namespace Toucan.Plugins;

/// <summary>Versioning for the plugin contract.</summary>
public static class PluginApi
{
    /// <summary>
    /// Version of the plugin API this build of Toucan implements. A plugin loads when its manifest <c>apiVersion</c>
    /// has the same major version and a minor version no newer than this one.
    /// </summary>
    public static readonly Version Current = new(1, 0);

    public static bool IsCompatible(Version pluginApiVersion)
    {
        ArgumentNullException.ThrowIfNull(pluginApiVersion);
        return pluginApiVersion.Major == Current.Major && pluginApiVersion.Minor <= Current.Minor;
    }
}
