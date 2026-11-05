using Toucan.Plugins;

namespace Toucan.Sample.Connector;

/// <summary>
/// A connector in miniature, written against the public content-source contract (plugin API 1.3). It registers one adapter and
/// nothing else: pulling, pushing, conflicts, accounts and the UI come from Toucan. Read docs/plugins.md, "Content sources".
/// </summary>
public sealed class ConnectorPlugin : IToucanPlugin
{
    public const string Id = "sample.connector";
    private readonly MemoryService _service;

    public ConnectorPlugin() : this(MemoryService.Shared) { }
    public ConnectorPlugin(MemoryService service) => _service = service;

    public void Initialize(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.AddSourceAdapter(new MemorySourceAdapter(_service));
    }
}
