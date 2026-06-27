namespace Toucan.Core.Tests;

/// <summary>
/// A synchronous IProgress implementation for tests.
/// Unlike Progress&lt;T&gt;, this invokes the callback directly without posting to SynchronizationContext.
/// </summary>
internal class DirectProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
