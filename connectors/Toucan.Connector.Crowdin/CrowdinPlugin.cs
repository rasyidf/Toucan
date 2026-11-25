using Toucan.Plugins;

namespace Toucan.Connector.Crowdin;

/// <summary>Registers the Crowdin adapter. Registration touches no network.</summary>
public sealed class CrowdinPlugin : IToucanPlugin
{
    public const string Id = "crowdin.connector";

    public void Initialize(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.AddSourceAdapter(new CrowdinAdapter());
    }
}
