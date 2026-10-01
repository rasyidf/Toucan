namespace Toucan.Core.Contracts.Services;

/// <summary>Categorizes how a translation item was changed.</summary>
public enum ChangeType
{
    /// <summary>The item was edited directly by a user in the editor.</summary>
    DirectEdit,

    /// <summary>The item value was populated by the pretranslation engine.</summary>
    Suggestion,

    /// <summary>The item was modified as part of a change request workflow.</summary>
    ChangeRequest
}
