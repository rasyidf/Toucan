using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.Avalonia.ViewModels;

/// <summary>A page button in the pagination bar. Number is 0 for ellipsis entries.</summary>
public sealed record PaginationButton(int Number, bool IsEllipsis, bool IsCurrent)
{
    public string Label => IsEllipsis ? "…" : Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Checkbox item for the "visible languages" filter.</summary>
public partial class LanguageVisibilityItem : ObservableObject
{
    [ObservableProperty] private string language = string.Empty;
    [ObservableProperty] private bool isVisible = true;
}

/// <summary>Observable key-value pair for editable provider options/secrets.</summary>
public partial class KeyValueItem : ObservableObject
{
    [ObservableProperty] private string key = string.Empty;
    [ObservableProperty] private string value = string.Empty;
    [ObservableProperty] private string hint = string.Empty;

    /// <summary>Whether this field is defined by the provider schema (not removable by the user).</summary>
    [ObservableProperty] private bool isSchemaField;

    public KeyValueItem() { }

    public KeyValueItem(string key, string value, string hint = "", bool isSchemaField = false)
    {
        Key = key;
        Value = value;
        Hint = hint;
        IsSchemaField = isSchemaField;
    }
}

/// <summary>Wrapper for a copy template string, enabling two-way binding in an ItemsControl.</summary>
public partial class CopyTemplateItem(string value) : ObservableObject
{
    [ObservableProperty] private string value = value;
}

/// <summary>A single validation issue shown in the Issues panel.</summary>
public sealed record ValidationIssueItem(
    string Key,
    string Message,
    Toucan.Core.Contracts.ValidationSeverity Severity = Toucan.Core.Contracts.ValidationSeverity.Warning,
    string? Namespace = null,
    string? Language = null,
    string? SuggestedFix = null,
    string? RuleId = null)
{
    public bool HasFix => !string.IsNullOrEmpty(SuggestedFix);
    public string SeverityLabel => Severity.ToString().ToUpperInvariant();
}

/// <summary>Built-in side panel descriptor registered in <see cref="Toucan.Core.Services.SidePanelRegistry"/>.</summary>
internal sealed class BuiltInSidePanel : Toucan.Core.Models.SidePanelBase
{
    public override string Id { get; }
    public override string Title { get; }
    public override string Icon { get; }
    public override Toucan.Core.Models.SidePanelSlot DefaultSlot { get; }

    public BuiltInSidePanel(string id, string title, string icon, Toucan.Core.Models.SidePanelSlot slot, int order)
    {
        Id = id;
        Title = title;
        Icon = icon;
        DefaultSlot = slot;
        Order = order;
    }
}
