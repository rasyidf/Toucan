using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.ViewModels;

/// <summary>
/// Observable key-value pair for editable provider options/secrets in the UI.
/// </summary>
public partial class KeyValueItem : ObservableObject
{
    [ObservableProperty]
    private string key = string.Empty;

    [ObservableProperty]
    private string value = string.Empty;

    /// <summary>Placeholder/description hint for the value field.</summary>
    [ObservableProperty]
    private string hint = string.Empty;

    /// <summary>Whether this field is defined by the provider schema (not removable by user).</summary>
    [ObservableProperty]
    private bool isSchemaField;

    public KeyValueItem() { }

    public KeyValueItem(string key, string value, string hint = "", bool isSchemaField = false)
    {
        Key = key;
        Value = value;
        Hint = hint;
        IsSchemaField = isSchemaField;
    }
}
