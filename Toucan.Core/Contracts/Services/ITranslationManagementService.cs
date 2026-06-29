using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

public interface ITranslationManagementService
{
    /// <summary>All translations in the current project.</summary>
    IReadOnlyList<TranslationItem> Translations { get; }

    /// <summary>Whether any item is dirty.</summary>
    bool IsDirty { get; }

    /// <summary>Raised when IsDirty changes. The boolean argument indicates the new IsDirty value.</summary>
#pragma warning disable CA1003 // Use generic event handler instances — design requires EventHandler<bool> for simplicity
    event EventHandler<bool>? DirtyStateChanged;
#pragma warning restore CA1003

    /// <summary>Loads translations and initializes baselines. Called by lifecycle service.</summary>
    void Initialize(IReadOnlyList<TranslationItem> items);

    /// <summary>Records a value change with debounce. Called by UI bindings.</summary>
    void NotifyValueChanged(TranslationItem item, string newValue);

    /// <summary>Records a comment change with debounce.</summary>
    void NotifyCommentChanged(TranslationItem item, string newComment);

    /// <summary>Returns all items whose current value/comment differs from baseline.</summary>
    IReadOnlyList<TranslationItem> GetDirtyItems();

    /// <summary>Marks all items as saved (updates baselines, clears dirty flags).</summary>
    void MarkAllSaved();

    /// <summary>Marks specific items as saved.</summary>
    void MarkSaved(IEnumerable<TranslationItem> items);

    /// <summary>Checks if a specific item is dirty.</summary>
    bool IsItemDirty(TranslationItem item);

    /// <summary>Resets all state (on project close).</summary>
    void Clear();

    /// <summary>Adds items to the collection (e.g., from merge).</summary>
    void AddItems(IEnumerable<TranslationItem> items);

    /// <summary>Removes items from the collection.</summary>
    void RemoveItems(Func<TranslationItem, bool> predicate);
}
