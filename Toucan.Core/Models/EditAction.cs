namespace Toucan.Core.Models;

/// <summary>
/// A single undoable edit action representing a translation value change.
/// </summary>
public record EditAction(string Namespace, string Language, string OldValue, string NewValue);
