using System.Windows.Controls;
using Toucan.ViewModels;

namespace Toucan.Views.Panels;

public partial class IssuesPanel : UserControl
{
    public IssuesPanel() => InitializeComponent();

    private void IssuesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IssuesList.SelectedItem is ValidationIssueItem issue && DataContext is MainWindowViewModel vm)
        {
            // Navigate to the key by setting the search text
            vm.SearchText = issue.Key;
        }
    }
}
