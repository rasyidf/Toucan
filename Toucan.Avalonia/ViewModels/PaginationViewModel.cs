using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toucan.Avalonia.ViewModels;

/// <summary>Client-side pagination over an in-memory list.</summary>
public partial class PaginationViewModel<T> : ObservableObject
{
    [ObservableProperty] private ObservableCollection<T> data = [];
    [ObservableProperty] private int page;
    [ObservableProperty] private int pageSize;
    [ObservableProperty] private int pages;
    [ObservableProperty] private bool isPartial;

    public ObservableCollection<T> PageData { get; private set; } = [];

    public bool HasPages => Pages > 1;
    public bool HasNextPage => Pages > Page;
    public bool HasPreviousPage => Page > 1;

    public int MaxItems { get; }
    public int TotalItems { get; private set; }

    public string PageMessage
    {
        get
        {
            if (Data.Count == 0) return "No results";
            if (!HasPages) return string.Create(CultureInfo.CurrentCulture, $"Showing all · {Data.Count} items");

            int start = ((Page - 1) * PageSize) + 1;
            int end = Math.Min(((Page - 1) * PageSize) + PageSize, Data.Count);
            return string.Create(CultureInfo.CurrentCulture, $"Page {Page} of {Pages} · {start}–{end} of {Data.Count}");
        }
    }

    public PaginationViewModel(int pageSize, IEnumerable<T> data, int maxItems = 100)
    {
        MaxItems = Math.Max(1, maxItems);
        UpdatePageSize(pageSize);
        SwapData(data);
    }

    private static int ComputePages(int total, int pageSize) =>
        pageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

    public void MoveFirst()
    {
        Page = 1;
        UpdatePageData();
    }

    public void LastPage()
    {
        Page = Pages;
        UpdatePageData();
    }

    public void NextPage()
    {
        if (Page < Pages) Page++;
        UpdatePageData();
    }

    public void PreviousPage()
    {
        if (Page > 1) Page--;
        UpdatePageData();
    }

    public void GoTo(int page)
    {
        Page = Math.Clamp(page, 1, Pages);
        UpdatePageData();
    }

    private void UpdatePageData()
    {
        if (Data.Count == 0)
        {
            PageData = [];
            OnPropertyChanged(nameof(PageData));
            return;
        }

        int startIndex = (Page - 1) * PageSize;
        if (startIndex >= Data.Count) startIndex = Math.Max(0, (Pages - 1) * PageSize);

        PageData = new ObservableCollection<T>(Data.Skip(startIndex).Take(PageSize));
        OnPropertyChanged(nameof(PageData));
    }

    partial void OnPageChanged(int value) => RaiseNavigationChanged();
    partial void OnPagesChanged(int value) => RaiseNavigationChanged();

    private void RaiseNavigationChanged()
    {
        OnPropertyChanged(nameof(PageMessage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasPages));
    }

    public void UpdatePageSize(int pageSize)
    {
        PageSize = pageSize <= 0 ? 30 : pageSize;
        Page = 1;
        Pages = ComputePages(Data.Count, PageSize);
        MoveFirst();
    }

    public void SwapData(IEnumerable<T> data, bool isPartial = false)
    {
        var list = data?.ToList() ?? [];
        TotalItems = list.Count;
        IsPartial = isPartial;
        Data = new ObservableCollection<T>(list);

        Pages = ComputePages(Data.Count, PageSize);
        Page = Math.Min(Math.Max(1, Page), Pages);
        UpdatePageData();
        RaiseNavigationChanged();
    }
}
