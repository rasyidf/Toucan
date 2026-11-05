namespace Toucan.Avalonia.ViewModels;

/// <summary>
/// The three operating modes of the translation editor.
/// Each mode changes which controls are editable and which panels are emphasized.
/// </summary>
public enum EditorMode
{
    /// <summary>Default mode — all values editable, MT suggestions shown.</summary>
    Editor,

    /// <summary>Review mode — source as reference, approve/reject prominent, validation in inspector.</summary>
    Review,

    /// <summary>Audit mode — all values read-only, shows change history and approval state.</summary>
    Audit
}

/// <summary>How a project moves from editing to sign-off.</summary>
public enum WorkflowMode
{
    /// <summary>Edit and save, like any text editor. No review or audit stages, no approval markers.</summary>
    Direct,

    /// <summary>Edit, then Review, then Audit, like the review ribbon in a word processor. Approvals follow the text.</summary>
    Pipelined
}
