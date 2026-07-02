using System.Windows;
using System.Windows.Controls;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs;

public partial class ProjectPropertiesWindow : FluentWindow
{
    private readonly StackPanel[] _pages;

    public ProjectPropertiesWindow(ProjectPropertiesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _pages = [PageIdentity, PageTranslation, PageEditor, PageFeatures, PageSourceCode];

        viewModel.CloseAction = result =>
        {
            DialogResult = result;
            Close();
        };
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages == null) return;

        int idx = NavList.SelectedIndex;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
