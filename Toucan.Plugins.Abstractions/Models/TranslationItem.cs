using Toucan.Core.Contracts.Services;

namespace Toucan.Core.Models;

public class TranslationItem
{
    public required string Language { get; set; }
    public ResourcePermissions Permissions { get; set; } = ResourcePermissions.Editable;
    private string _namespace = string.Empty;
    public string Namespace
    {
        get => _namespace;
        set
        {
            if (!Permissions.EditKeys) return;
            if (Identity is { } identity && value != _namespace && value != identity.Selector)
                Identity = ResourceEntryOperations.Rename(identity, value);
            _namespace = value;
        }
    }
    public ResourceEntryIdentity? Identity { get; set; }
    private string _value = string.Empty;
    public string Value { get => _value; set { if (Permissions.EditValues) _value = value; } }
    private string _comment = string.Empty;
    public string Comment { get => _comment; set { if (Permissions.EditMetadata) _comment = value; } }
    private bool _approved;
    public bool IsApproved { get => _approved; set { if (Permissions.EditMetadata) _approved = value; } }

    /// <summary>
    /// Format-specific data a load strategy keeps so its save strategy can write the entry back
    /// unchanged (for PO: context, plural forms, flags, references, header, source file).
    /// Null for formats that need none.
    /// </summary>
    public Dictionary<string, string>? FormatData { get; set; }

    // Audit metadata
    private DateTime? _modified;
    public DateTime? LastModifiedUtc { get => _modified; set { if (Permissions.EditMetadata) _modified = value; } }
    private DateTime? _approvedAt;
    public DateTime? ApprovedAtUtc { get => _approvedAt; set { if (Permissions.EditMetadata) _approvedAt = value; } }
    private ChangeType _changeType;
    public ChangeType ChangeType { get => _changeType; set { if (Permissions.EditMetadata) _changeType = value; } }
}
