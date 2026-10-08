using Toucan.Plugins;

namespace Toucan.Sample.Plugin;

/// <summary>
/// The plugin's entry point. Toucan creates it with the parameterless constructor and calls
/// <see cref="Initialize"/> once at startup. Register everything here; nothing is active unless this returns normally.
/// </summary>
public sealed class SamplePlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // One class may implement both directions of a format; they must report the same FormatId.
        var tsv = new TsvFormat();
        context.AddFormat(save: tsv, load: tsv);

        // Rule IDs are global. Prefix yours with the plugin ID so they cannot collide with built-ins or other plugins.
        context.AddValidationRule(new TodoMarkerRule());

        // Commands show up in the palette, the Extensions menu and the shortcut list. IDs start with the plugin ID.
        context.AddCommand(StampCommand.Definition, new StampCommand());
    }
}
