using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>
/// Manages in-memory translation collection, per-item dirty tracking with baseline comparison,
/// and debounced change notification.
/// </summary>
public class TranslationManagementService : ITranslationManagementService, IDisposable
{
    private readonly IUndoRedoService _undoRedoService;
    private readonly List<TranslationItem> _translations = [];
    private readonly Dictionary<(string Language, string Namespace), TranslationBaseline> _baselines = new();
    private readonly object _lock = new();

    private Timer? _debounceTimer;
    private bool _lastDirtyState;
    private bool _disposed;

    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    public TranslationManagementService(IUndoRedoService undoRedoService)
    {
        _undoRedoService = undoRedoService;
    }

    /// <inheritdoc/>
    public IReadOnlyList<TranslationItem> Translations
    {
        get
        {
            lock (_lock)
            {
                return _translations.AsReadOnly();
            }
        }
    }

    /// <inheritdoc/>
    public bool IsDirty => GetDirtyItems().Count > 0;

    /// <inheritdoc/>
#pragma warning disable CA1003
    public event EventHandler<bool>? DirtyStateChanged;
#pragma warning restore CA1003

    /// <inheritdoc/>
    public void Initialize(IReadOnlyList<TranslationItem> items)
    {
        lock (_lock)
        {
            _translations.Clear();
            _baselines.Clear();
            _translations.AddRange(items);

            foreach (var item in items)
            {
                var key = (item.Language, item.Namespace);
                _baselines[key] = new TranslationBaseline
                {
                    Language = item.Language,
                    Namespace = item.Namespace,
                    SavedValue = item.Value,
                    SavedComment = item.Comment
                };
            }

            _lastDirtyState = false;
        }
    }

    /// <inheritdoc/>
    public void NotifyValueChanged(TranslationItem item, string newValue)
    {
        item.Value = newValue;
        ScheduleDirtyCheck();
    }

    /// <inheritdoc/>
    public void NotifyCommentChanged(TranslationItem item, string newComment)
    {
        item.Comment = newComment;
        ScheduleDirtyCheck();
    }

    /// <inheritdoc/>
    public IReadOnlyList<TranslationItem> GetDirtyItems()
    {
        lock (_lock)
        {
            var dirty = new List<TranslationItem>();

            foreach (var item in _translations)
            {
                var key = (item.Language, item.Namespace);
                if (_baselines.TryGetValue(key, out var baseline))
                {
                    if (!string.Equals(item.Value, baseline.SavedValue, StringComparison.Ordinal) ||
                        !string.Equals(item.Comment, baseline.SavedComment, StringComparison.Ordinal))
                    {
                        dirty.Add(item);
                    }
                }
                else
                {
                    // Item has no baseline — it was added after initialization, so it's dirty.
                    dirty.Add(item);
                }
            }

            return dirty;
        }
    }

    /// <inheritdoc/>
    public bool IsItemDirty(TranslationItem item)
    {
        lock (_lock)
        {
            var key = (item.Language, item.Namespace);
            if (_baselines.TryGetValue(key, out var baseline))
            {
                return !string.Equals(item.Value, baseline.SavedValue, StringComparison.Ordinal) ||
                       !string.Equals(item.Comment, baseline.SavedComment, StringComparison.Ordinal);
            }

            // No baseline means the item was added after init — it's dirty.
            return true;
        }
    }

    /// <inheritdoc/>
    public void MarkAllSaved()
    {
        lock (_lock)
        {
            _baselines.Clear();

            foreach (var item in _translations)
            {
                var key = (item.Language, item.Namespace);
                _baselines[key] = new TranslationBaseline
                {
                    Language = item.Language,
                    Namespace = item.Namespace,
                    SavedValue = item.Value,
                    SavedComment = item.Comment
                };
            }
        }

        RaiseDirtyStateChangedIfNeeded();
    }

    /// <inheritdoc/>
    public void MarkSaved(IEnumerable<TranslationItem> items)
    {
        lock (_lock)
        {
            foreach (var item in items)
            {
                var key = (item.Language, item.Namespace);
                _baselines[key] = new TranslationBaseline
                {
                    Language = item.Language,
                    Namespace = item.Namespace,
                    SavedValue = item.Value,
                    SavedComment = item.Comment
                };
            }
        }

        RaiseDirtyStateChangedIfNeeded();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (_lock)
        {
            _translations.Clear();
            _baselines.Clear();
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _lastDirtyState = false;
        }
    }

    /// <inheritdoc/>
    public void AddItems(IEnumerable<TranslationItem> items)
    {
        lock (_lock)
        {
            foreach (var item in items)
            {
                _translations.Add(item);
                // New items have no baseline, so they'll be considered dirty
                // until MarkSaved/MarkAllSaved is called.
            }
        }

        RaiseDirtyStateChangedIfNeeded();
    }

    /// <inheritdoc/>
    public void RemoveItems(Func<TranslationItem, bool> predicate)
    {
        lock (_lock)
        {
            var toRemove = _translations.Where(predicate).ToList();
            foreach (var item in toRemove)
            {
                _translations.Remove(item);
                var key = (item.Language, item.Namespace);
                _baselines.Remove(key);
            }
        }

        RaiseDirtyStateChangedIfNeeded();
    }

    /// <summary>
    /// Schedules a dirty-state check after the debounce interval (500ms).
    /// Resets the timer if called again before it fires.
    /// </summary>
    private void ScheduleDirtyCheck()
    {
        lock (_lock)
        {
            if (_disposed) return;

            if (_debounceTimer is null)
            {
                _debounceTimer = new Timer(OnDebounceElapsed, null, DebounceInterval, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _debounceTimer.Change(DebounceInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        RaiseDirtyStateChangedIfNeeded();
    }

    /// <summary>
    /// Checks the current dirty state and raises DirtyStateChanged if it transitioned.
    /// </summary>
    private void RaiseDirtyStateChangedIfNeeded()
    {
        bool currentDirty = IsDirty;
        bool shouldRaise;

        lock (_lock)
        {
            shouldRaise = currentDirty != _lastDirtyState;
            _lastDirtyState = currentDirty;
        }

        if (shouldRaise)
        {
            DirtyStateChanged?.Invoke(this, currentDirty);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        GC.SuppressFinalize(this);
    }
}
