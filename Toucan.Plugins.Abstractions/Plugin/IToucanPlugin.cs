namespace Toucan.Plugins;

/// <summary>
/// Entry point of a plugin assembly. The host finds the single public, non-abstract implementation in the
/// assembly named by the manifest's <c>entryAssembly</c>, creates it with a parameterless constructor and calls
/// <see cref="Initialize"/> once at startup, before the application container is built.
/// </summary>
public interface IToucanPlugin
{
    void Initialize(IPluginContext context);
}
