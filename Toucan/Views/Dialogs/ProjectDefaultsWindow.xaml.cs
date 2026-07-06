using System.Windows;
using System.Windows.Controls;
using Toucan.ViewModels;
using Wpf.Ui.Controls;

namespace Toucan.Views.Dialogs;

public partial class ProjectDefaultsWindow : FluentWindow
{
    private readonly UIElement[] _pages;

    public ProjectDefaultsWindow(ProjectDefaultsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _pages = [PageEditor, PageTranslation, PageValidation, PageSourceCode, PageFeatures];

        viewModel.CloseAction = result =>
        {
            DialogResult = result;
        };
    }

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_pages == null) return;
        int idx = NavList.SelectedIndex;
        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
    }
}
