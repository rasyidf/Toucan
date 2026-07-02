using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Toucan.Core.Contracts;
using Toucan.ViewModels;

namespace Toucan.Views.Panels;

public partial class SourceCodePanel : UserControl
{
    public SourceCodePanel() => InitializeComponent();

    private ListView UsagesListView => (ListView)FindName("UsagesList");
    private TextBox FilterTextBox => (TextBox)FindName("FilterBox");

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var view = CollectionViewSource.GetDefaultView(UsagesListView?.ItemsSource);
        if (view == null) return;

        var filter = FilterTextBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(filter))
        {
            view.Filter = null;
        }
        else
        {
            view.Filter = obj => obj is KeyUsage usage &&
                (usage.Key.Contains(filter, System.StringComparison.OrdinalIgnoreCase) ||
                 usage.FilePath.Contains(filter, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    private void UsagesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (UsagesListView?.SelectedItem is KeyUsage usage && DataContext is MainWindowViewModel vm)
        {
            vm.OpenInEditorCommand.Execute(usage.Key);
        }
    }
}
