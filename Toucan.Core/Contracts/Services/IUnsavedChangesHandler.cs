namespace Toucan.Core.Contracts.Services;

/// <summary>
/// Choices presented to the user when closing a project with unsaved changes.
/// </summary>
public enum UnsavedChangesChoice
{
    /// <summary>Save changes before closing.</summary>
    Save,

    /// <summary>Discard changes and close without saving.</summary>
    Discard,

    /// <summary>Cancel the close operation.</summary>
    Cancel
}

/// <summary>
/// Abstraction for prompting the user about unsaved changes.
/// The WPF layer implements this to show a modal dialog.
/// </summary>
public interface IUnsavedChangesHandler
{
    /// <summary>
    /// Prompts the user to choose how to handle unsaved changes.
    /// Returns the user's choice (Save, Discard, or Cancel).
    /// </summary>
    Task<UnsavedChangesChoice> PromptAsync();
}
