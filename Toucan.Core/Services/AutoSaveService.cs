using Microsoft.Extensions.Logging;
using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Services;

/// <summary>
/// Timer-based auto-save service that periodically persists unsaved changes.
/// Uses Lazy&lt;IProjectLifecycleService&gt; to break potential circular dependency.
/// </summary>
public sealed class AutoSaveService : IAutoSaveService, IDisposable
{
    private readonly Lazy<IProjectLifecycleService> _lifecycleService;
    private readonly ITranslationManagementService _translationManagement;
    private readonly IFileWatcherService _fileWatcher;
    private readonly ILogger<AutoSaveService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private Timer? _timer;
    private TimeSpan _interval;
    private bool _disposed;

    public AutoSaveService(
        Lazy<IProjectLifecycleService> lifecycleService,
        ITranslationManagementService translationManagement,
        IFileWatcherService fileWatcher,
        ILogger<AutoSaveService> logger)
    {
        _lifecycleService = lifecycleService;
        _translationManagement = translationManagement;
        _fileWatcher = fileWatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _timer is not null;

    /// <inheritdoc />
    public event EventHandler<string>? AutoSaveFailed;

    /// <inheritdoc />
    public void Start(TimeSpan interval)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var clampedSeconds = Math.Clamp((int)interval.TotalSeconds, 10, 600);
        _interval = TimeSpan.FromSeconds(clampedSeconds);

        _timer?.Dispose();
        _timer = new Timer(OnTimerTick, null, _interval, Timeout.InfiniteTimeSpan);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Auto-save started with interval {Seconds}s", clampedSeconds);
    }

    /// <inheritdoc />
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _timer?.Dispose();
        _timer = null;

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Auto-save stopped");
    }

    /// <inheritdoc />
    public void ResetTimer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_timer is null)
            return;

        _timer.Change(_interval, Timeout.InfiniteTimeSpan);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Auto-save timer reset to {Seconds}s", _interval.TotalSeconds);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        _semaphore.Dispose();
    }

    private async void OnTimerTick(object? state)
    {
        if (_disposed)
            return;

        // Skip if nothing is dirty
        if (!_translationManagement.IsDirty)
        {
            ScheduleNextTick();
            return;
        }

        // Guard against concurrent execution
        if (!_semaphore.Wait(0))
        {
            ScheduleNextTick();
            return;
        }

        try
        {
            var result = await _lifecycleService.Value.SaveProjectAsync().ConfigureAwait(false);

            if (result.Status == ProjectSaveStatus.Success)
            {
                _fileWatcher.TakeSnapshot();

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Auto-save completed successfully");
            }
            else
            {
                var errorMessage = result.ErrorMessage ?? "Auto-save failed with unknown error";
                _logger.LogWarning("Auto-save failed: {Error}", errorMessage);
                AutoSaveFailed?.Invoke(this, errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-save failed with exception");
            AutoSaveFailed?.Invoke(this, ex.Message);
        }
        finally
        {
            _semaphore.Release();
            ScheduleNextTick();
        }
    }

    private void ScheduleNextTick()
    {
        if (_disposed || _timer is null)
            return;

        try
        {
            _timer.Change(_interval, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Timer was disposed between the null check and Change call — safe to ignore
        }
    }
}
