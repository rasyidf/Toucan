using System.Collections.ObjectModel;
using Toucan.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Toucan.Views.Components;

public partial class PaginationControl : UserControl
{
    public PaginationControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty PageButtonsProperty = DependencyProperty.Register(nameof(PageButtons), typeof(ObservableCollection<PaginationButton>), typeof(PaginationControl), new PropertyMetadata(null));
    public ObservableCollection<PaginationButton> PageButtons
    {
        get => (ObservableCollection<PaginationButton>)GetValue(PageButtonsProperty);
        set => SetValue(PageButtonsProperty, value);
    }

    public static readonly DependencyProperty PageMessageProperty = DependencyProperty.Register(nameof(PageMessage), typeof(string), typeof(PaginationControl), new PropertyMetadata(string.Empty));
    public string PageMessage
    {
        get => (string)GetValue(PageMessageProperty);
        set => SetValue(PageMessageProperty, value);
    }

    public static readonly DependencyProperty HasPreviousPageProperty = DependencyProperty.Register(nameof(HasPreviousPage), typeof(bool), typeof(PaginationControl), new PropertyMetadata(false));
    public bool HasPreviousPage
    {
        get => (bool)GetValue(HasPreviousPageProperty);
        set => SetValue(HasPreviousPageProperty, value);
    }

    public static readonly DependencyProperty HasNextPageProperty = DependencyProperty.Register(nameof(HasNextPage), typeof(bool), typeof(PaginationControl), new PropertyMetadata(false));
    public bool HasNextPage
    {
        get => (bool)GetValue(HasNextPageProperty);
        set => SetValue(HasNextPageProperty, value);
    }

    public static readonly DependencyProperty IsPartialProperty = DependencyProperty.Register(nameof(IsPartial), typeof(bool), typeof(PaginationControl), new PropertyMetadata(false));
    public bool IsPartial
    {
        get => (bool)GetValue(IsPartialProperty);
        set => SetValue(IsPartialProperty, value);
    }

    public static readonly DependencyProperty FirstPageCommandProperty = DependencyProperty.Register(nameof(FirstPageCommand), typeof(ICommand), typeof(PaginationControl), new PropertyMetadata(null));
    public ICommand FirstPageCommand
    {
        get => (ICommand)GetValue(FirstPageCommandProperty);
        set => SetValue(FirstPageCommandProperty, value);
    }

    public static readonly DependencyProperty PreviousPageCommandProperty = DependencyProperty.Register(nameof(PreviousPageCommand), typeof(ICommand), typeof(PaginationControl), new PropertyMetadata(null));
    public ICommand PreviousPageCommand
    {
        get => (ICommand)GetValue(PreviousPageCommandProperty);
        set => SetValue(PreviousPageCommandProperty, value);
    }

    public static readonly DependencyProperty NextPageCommandProperty = DependencyProperty.Register(nameof(NextPageCommand), typeof(ICommand), typeof(PaginationControl), new PropertyMetadata(null));
    public ICommand NextPageCommand
    {
        get => (ICommand)GetValue(NextPageCommandProperty);
        set => SetValue(NextPageCommandProperty, value);
    }

    public static readonly DependencyProperty LastPageCommandProperty = DependencyProperty.Register(nameof(LastPageCommand), typeof(ICommand), typeof(PaginationControl), new PropertyMetadata(null));
    public ICommand LastPageCommand
    {
        get => (ICommand)GetValue(LastPageCommandProperty);
        set => SetValue(LastPageCommandProperty, value);
    }

    public static readonly DependencyProperty GoToPageCommandProperty = DependencyProperty.Register(nameof(GoToPageCommand), typeof(ICommand), typeof(PaginationControl), new PropertyMetadata(null));
    public ICommand GoToPageCommand
    {
        get => (ICommand)GetValue(GoToPageCommandProperty);
        set => SetValue(GoToPageCommandProperty, value);
    }

    public static readonly DependencyProperty ShowAllCommandProperty = DependencyProperty.Register(nameof(ShowAllCommand), typeof(ICommand), typeof(PaginationControl), new PropertyMetadata(null));
    public ICommand ShowAllCommand
    {
        get => (ICommand)GetValue(ShowAllCommandProperty);
        set => SetValue(ShowAllCommandProperty, value);
    }
}
