namespace Toucan.Plugins.Desktop;

/// <summary>Versioning for the desktop contract, independent of the plugin API (<c>PluginApi</c>).</summary>
public static class DesktopContract
{
    /// <summary>
    /// Version of the desktop contract this build of Toucan implements. A desktop assembly loads when its manifest
    /// <c>desktop.contractVersion</c> has the same major version and a minor version no newer than this one.
    /// </summary>
    public static readonly Version Current = new(1, 0);

    public static bool IsCompatible(Version pluginContractVersion)
    {
        ArgumentNullException.ThrowIfNull(pluginContractVersion);
        return pluginContractVersion.Major == Current.Major && pluginContractVersion.Minor <= Current.Minor;
    }
}

/// <summary>
/// Theme resource keys plugin views can rely on. Use them with <c>DynamicResource</c> so a view follows light and dark
/// mode; other keys of the host theme may change between releases.
/// </summary>
public static class DesktopTheme
{
    public const string TextBrush = "TextBrush";
    public const string MutedTextBrush = "MutedTextBrush";
    public const string FaintTextBrush = "FaintTextBrush";
    public const string CardBackgroundBrush = "CardBackgroundBrush";
    public const string CardBorderBrush = "CardBorderBrush";
    public const string SubtleBorderBrush = "SubtleBorderBrush";
    public const string GoodBrush = "GoodBrush";
    public const string WarnBrush = "WarnBrush";
    public const string BadBrush = "BadBrush";

    public static IReadOnlyList<string> All { get; } =
        [TextBrush, MutedTextBrush, FaintTextBrush, CardBackgroundBrush, CardBorderBrush, SubtleBorderBrush, GoodBrush, WarnBrush, BadBrush];
}
