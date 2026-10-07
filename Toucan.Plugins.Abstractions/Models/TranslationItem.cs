using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Models;

public class TranslationItem
{
    public required string Language { get; set; }
    public string Namespace { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsApproved { get; set; }

    /// <summary>
    /// Format-specific data a load strategy keeps so its save strategy can write the entry back
    /// unchanged (for PO: context, plural forms, flags, references, header, source file).
    /// Null for formats that need none.
    /// </summary>
    public Dictionary<string, string>? FormatData { get; set; }

    // Audit metadata
    public DateTime? LastModifiedUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public ChangeType ChangeType { get; set; } = ChangeType.DirectEdit;
}
