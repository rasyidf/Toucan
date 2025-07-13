using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Toucan.Core.Services;

public partial class PagingController<T> : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<T> data;
    public ObservableCollection<T> PageData { get; private set; }

    [ObservableProperty]
    private int page;
    [ObservableProperty]
    private int pageSize;
    [ObservableProperty]
    private int pages;

    [ObservableProperty]
    private bool isPartial;
    public bool HasPages => Pages > 1;
    public bool HasNextPage => Pages > Page;
    public bool HasPreviousPage => Page > 1;
    public string PageMessage
    {
        get
        {
            if (Data == null)
                return "No Results";
            if (!HasPages && !IsPartial)
                return "Showing All | " + Data.Count();

            var pagingMsg = $"Showing Page {Page} of {Pages} | {(Page - 1) * PageSize}-{Clamp((Page - 1) * PageSize + PageSize, Data.Count())} of {Data.Count()}";

            return pagingMsg; ;
        }
    }

    private static int Clamp(int val, int max)
    {
        if (val > max)
            return max;
        return val;
    }
    public PagingController(int pageSize, IEnumerable<T> data)
    {
        UpdatePageSize(pageSize);
        SwapData(data);
    }


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

    private void UpdatePageData()
    {
        PageData = new (Data.AsEnumerable().Skip((Page - 1) * PageSize).Take(PageSize).ToList());
    }

    public void NextPage()
    {
        Page++;
        if (Page >= Pages)
        {
            Page = Pages;
        }
        UpdatePageData();
    }
    public void PreviousPage()
    {
        Page--;
        if (Page < 1)
            Page = 1;
        UpdatePageData();
    }

    public void UpdatePageSize(int pageSize)
    {

        PageSize = pageSize;
        Page = 1;
        Data ??= [];
        double pages = Data.Count() / (double)PageSize;
        if (pages > (int)pages)
            pages++;

        Pages = (int)pages;
        MoveFirst();

    }
    public void SwapData(IEnumerable<T> data, bool isPartial = false)
    {
        IsPartial = isPartial;
        Data = new (data);
        UpdatePageSize(PageSize);
    }


}
