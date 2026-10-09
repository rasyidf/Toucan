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
        context.AddCommand(StampCommand.Definition, new StampCommand(context.Services));

        // Typed settings: the desktop app builds a form for them on the Plugins page of Settings, and stores them (outside the
        // plugin folder) with validation, defaults and migrations. Raise Version and add a ConfigMigration when a change would
        // misread values saved by an older version.
        context.SetConfiguration(new ConfigSchema
        {
            Version = 1,
            Fields =
            [
                new ConfigField { Key = "greeting", Label = "Greeting", Description = "Shown in the status bar and in notifications.", Default = "Hello", Required = true, Maximum = 40 },
                new ConfigField
                {
                    Key = "mode", Label = "Mode", Type = ConfigFieldType.Choice, Default = "safe",
                    Choices = [new ConfigChoice("safe", "Safe"), new ConfigChoice("fast", "Fast")],
                },
                new ConfigField { Key = "intervalSeconds", Label = "Check interval (seconds)", Type = ConfigFieldType.WholeNumber, Default = 30L, Minimum = 5, Maximum = 3600 },
                new ConfigField { Key = "serverUrl", Label = "Server address", Type = ConfigFieldType.Url, Description = "An http or https address." },
                new ConfigField { Key = "apiKey", Label = "API key", Type = ConfigFieldType.Secret, Description = "Kept in the encrypted secret store, never in the settings file or the logs." },
                new ConfigField { Key = "branch", Label = "Branch (this project)", Scope = ConfigScope.Workspace, Default = "main", Description = "Remembered per project." },
            ],
        });
    }
}
