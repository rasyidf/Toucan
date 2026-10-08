namespace Toucan.Core.Models;

/// <summary>How safely a format can be edited and saved.</summary>
public enum FormatEditing
{
    /// <summary>Everything the format stores for a translation survives a load and save.</summary>
    Full,
    /// <summary>Strings are editable, but the constructs listed in <see cref="FormatSupport.Unsupported"/> are not written back.</summary>
    Limited,
    /// <summary>Files can be read but not saved; Toucan refuses to write them.</summary>
    ReadOnly,
}

/// <summary>
/// What a format keeps and what it drops. Shown in the format support matrix (docs/formats.md) and in the warning
/// when a project opens in a <see cref="FormatEditing.Limited"/> format.
/// </summary>
/// <param name="Editing">How safely the format can be saved.</param>
/// <param name="Versions">The dialects or versions that load and save correctly.</param>
/// <param name="Preserved">Constructs that survive load and save (plurals, comments, metadata, layout).</param>
/// <param name="Unsupported">Constructs that are dropped or rewritten on save, or cannot be edited.</param>
public sealed record FormatSupport(
    FormatEditing Editing,
    string Versions,
    IReadOnlyList<string> Preserved,
    IReadOnlyList<string> Unsupported);
