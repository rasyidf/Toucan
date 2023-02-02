namespace OPEdit.Core.Services;

public class PagingController<T>
{
    public IEnumerable<T> Data { get; private set; }
    public IEnumerable<T> PageData { get; private set; }

    public int Page { get; private set; }
    public int PageSize { get; private set; }
    public int Pages { get; private set; }
    public bool IsPartial { get; private set; }
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
        PageData = Data.Skip((Page - 1) * PageSize).Take(PageSize).ToList();
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
        if (Data == null)
            Data = new List<T>();
        double pages = Data.Count() / (double)PageSize;
        if (pages > (int)pages)
            pages++;

        Pages = (int)pages;
        MoveFirst();

    }
    public void SwapData(IEnumerable<T> data, bool isPartial = false)
    {
        IsPartial = isPartial;
        Data = data;
        UpdatePageSize(PageSize);
    }


}
