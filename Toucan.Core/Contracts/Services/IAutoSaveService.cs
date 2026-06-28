namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Manages timer-based periodic persistence of unsaved translation changes.
/// </summary>
public interface IAutoSaveService
{
    /// <summary>Starts the auto-save timer with the given interval.</summary>
    void Start(TimeSpan interval);

    /// <summary>Stops the auto-save timer.</summary>
#pragma warning disable CA1716 // Identifiers should not match keywords — Stop is the natural verb for timer control
    void Stop();
#pragma warning restore CA1716

    /// <summary>Resets the timer to start a fresh interval (called after manual save).</summary>
    void ResetTimer();

    /// <summary>Whether auto-save is currently active.</summary>
    bool IsEnabled { get; }

    /// <summary>Raised when an auto-save operation fails (for non-blocking UI notification).</summary>
#pragma warning disable CA1003 // Use generic event handler instances — design requires EventHandler<string> for simplicity
    event EventHandler<string>? AutoSaveFailed;
#pragma warning restore CA1003
}
