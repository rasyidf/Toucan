using Avalonia.Threading;
using Toucan.Core.Contracts.Services;

namespace Toucan.Avalonia.Services;

/// <summary>Save / Don't Save / Cancel prompt used by the lifecycle service before closing a dirty project.</summary>
internal sealed class UnsavedChangesHandler(IAsyncMessageService messages) : IUnsavedChangesHandler
{
    public async Task<UnsavedChangesChoice> PromptAsync()
    {
        var choice = await Dispatcher.UIThread.InvokeAsync(() =>
            messages.ChooseAsync("You have unsaved changes. Do you want to save them before continuing?", "Unsaved Changes", "Save", "Don't Save"));
        return choice switch
        {
            ChoiceResult.Primary => UnsavedChangesChoice.Save,
            ChoiceResult.Secondary => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel
        };
    }
}

/// <summary>Reload / Merge / Ignore prompt shown when files change on disk while there are unsaved edits.</summary>
internal sealed class ExternalChangeHandler(IAsyncMessageService messages) : IExternalChangeHandler
{
    public async Task<ExternalChangeChoice> PromptAsync()
    {
        var choice = await Dispatcher.UIThread.InvokeAsync(() =>
            messages.ChooseAsync("Translation files were changed outside Toucan, and you have unsaved edits.\n\nReload discards your edits. Merge keeps both where they don't conflict.",
                "Files Changed on Disk", "Merge", "Reload", "Ignore"));
        return choice switch
        {
            ChoiceResult.Primary => ExternalChangeChoice.Merge,
            ChoiceResult.Secondary => ExternalChangeChoice.Reload,
            _ => ExternalChangeChoice.Ignore
        };
    }

    public async Task<IReadOnlyList<DiffEntry>?> ShowConflictResolutionAsync(IReadOnlyList<DiffEntry> conflicts)
    {
        var accept = await Dispatcher.UIThread.InvokeAsync(() =>
            messages.ConfirmAsync($"{conflicts.Count} value(s) were changed both here and on disk.\n\nUse the version from disk for these?",
                "Merge Conflicts", "Use Disk Version", "Keep Mine"));
        return accept ? conflicts : null;
    }
}
