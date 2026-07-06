using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.ViewModels;

/// <summary>
/// Wrapper for a copy template string, enabling two-way binding in ItemsControl.
/// </summary>
public partial class CopyTemplateItem : ObservableObject
{
    [ObservableProperty] private string value;

    public CopyTemplateItem(string value) => this.value = value;
}
