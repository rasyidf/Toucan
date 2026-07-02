using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Toucan.Services;
using Toucan.ViewModels;

namespace Toucan.Views;

public partial class InspectorView : UserControl
{
    public InspectorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PanelService.Instance.PropertyChanged += OnPanelServicePropertyChanged;
        SyncTabToEditorMode(PanelService.Instance.EditorMode);
    }

    private void OnPanelServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelService.EditorMode))
        {
            SyncTabToEditorMode(PanelService.Instance.EditorMode);
        }
    }

    private void SyncTabToEditorMode(EditorMode mode)
    {
        InspectorTabs.SelectedIndex = mode switch
        {
            EditorMode.Editor => 1, // Suggestions
            EditorMode.Review => 3, // Validation
            EditorMode.Audit  => 2, // Details
            _ => 0
        };
    }
}
