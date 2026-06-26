using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Toucan.Avalonia.ViewModels;

public partial class PaginationViewModel<T> : ObservableObject
{
    [ObservableProperty] private ObservableCollection<T> data = new();
    public ObservableCollection<T> PageData { get; private set; } = new();
    [ObservableProperty] private int page;
    [ObservableProperty] private int pageSize;
    [ObservableProperty] private int pages;
    [ObservableProperty] private bool isPartial;

    public bool HasPages => Pages > 1;
    public bool HasNextPage => Pages > Page;
    public bool HasPreviousPage => Page > 1;
    public int MaxItems { get; }
    public int TotalItems { get; private set; }

    public string PageMessage
    {
        get
        {
            if (Data == null || Data.Count == 0) return "No Results";
            if (!HasPages && !IsPartial) return $"Showing All | {Data.Count}";
            int start = ((Page - 1) * PageSize) + 1;
            int end = Math.Min((Page - 1) * PageSize + PageSize, Data.Count);
            return IsPartial && TotalItems > Data.Count
                ? $"Showing Page {Page} of {Pages} | {start}-{end} of {Data.Count} (truncated from {TotalItems})"
                : $"Showing Page {Page} of {Pages} | {start}-{end} of {Data.Count}";
        }
    }

    public PaginationViewModel(int pageSize, IEnumerable<T> data, int maxItems = 100)
    {
        MaxItems = Math.Max(1, maxItems);
        UpdatePageSize(pageSize);
        SwapData(data ?? Enumerable.Empty<T>());
    }

    public void MoveFirst() { Page = 1; UpdatePageData(); }
    public void LastPage() { Page = Pages; UpdatePageData(); }
    public void NextPage() { if (Page < Pages) Page++; UpdatePageData(); }
    public void PreviousPage() { if (Page > 1) Page--; UpdatePageData(); }

    partial void OnPageChanged(int value) => NotifyPaging();
    partial void OnPagesChanged(int value) => NotifyPaging();

    private void NotifyPaging()
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
        Data ??= new ObservableCollection<T>();
        Pages = ComputePages(Data.Count, PageSize);
        MoveFirst();
    }

    public void SwapData(IEnumerable<T> data, bool isPartial = false)
    {
        var list = data?.ToList() ?? [];
        TotalItems = list.Count;
        if (TotalItems > MaxItems) { IsPartial = true; Data = new ObservableCollection<T>(list.Take(MaxItems)); }
        else { IsPartial = isPartial; Data = new ObservableCollection<T>(list); }
        Pages = ComputePages(Data.Count, PageSize);
        Page = Math.Min(Math.Max(1, Page), Pages);
        UpdatePageData();
        NotifyPaging();
        OnPropertyChanged(nameof(IsPartial));
    }

    private void UpdatePageData()
    {
        if (Data == null || Data.Count == 0) { PageData = new(); OnPropertyChanged(nameof(PageData)); return; }
        int startIndex = Math.Min((Page - 1) * PageSize, Math.Max(0, Data.Count - 1));
        PageData = new ObservableCollection<T>(Data.Skip(startIndex).Take(PageSize));
        OnPropertyChanged(nameof(PageData));
    }

    private static int ComputePages(int total, int pageSize) => pageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
}
