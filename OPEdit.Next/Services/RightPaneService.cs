using System.Windows.Controls;
using System.Windows.Navigation;

using MahApps.Metro.Controls;

using OPEdit.Contracts.Services;
using OPEdit.Contracts.ViewModels;
using Wpf.Ui.Controls;

namespace OPEdit.Services;

public class RightPaneService : IRightPaneService
{
    private readonly IPageService _pageService;
    private Frame _frame;
    private object _lastParameterUsed;
    private Dialog _dialogView;

    public event EventHandler PaneOpened;

    public event EventHandler PaneClosed;

    public RightPaneService(IPageService pageService)
    {
        _pageService = pageService;
    }

    public void Initialize(Frame rightPaneFrame, Dialog splitView)
    {
        _frame = rightPaneFrame;
        _dialogView = splitView;
        _frame.Navigated += OnNavigated;
        _dialogView.Closed += OnPaneClosed;
    }

    public void CleanUp()
    {
        _frame.Navigated -= OnNavigated;
        _dialogView.Closed -= OnPaneClosed;
    }

    public void OpenDialog(string pageKey, object parameter = null)
    {
        var pageType = _pageService.GetPageType(pageKey);
        if (_frame.Content?.GetType() != pageType || (parameter != null && !parameter.Equals(_lastParameterUsed)))
        {
            var page = _pageService.GetPage(pageKey);
            var navigated = _frame.Navigate(page, parameter);
            if (navigated)
            {
                _lastParameterUsed = parameter;
                var dataContext = _frame.GetDataContext();
                if (dataContext is INavigationAware navigationAware)
                {
                    navigationAware.OnNavigatedFrom();
                }
            }
        }

        _dialogView.ShowAndWaitAsync();
        PaneOpened?.Invoke(_dialogView, EventArgs.Empty);
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        if (sender is Frame frame)
        {
            frame.CleanNavigation();
            var dataContext = frame.GetDataContext();
            if (dataContext is INavigationAware navigationAware)
            {
                navigationAware.OnNavigatedTo(e.ExtraData);
            }
        }
    }

    private void OnPaneClosed(object sender, EventArgs e)
        => PaneClosed?.Invoke(sender, e);
}
