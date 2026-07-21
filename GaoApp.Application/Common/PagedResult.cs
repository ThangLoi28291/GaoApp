using System;

namespace GaoApp.Application.Common;

public class PagedResult<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public List<T> Items { get; set; } = new();

    public int TotalPages => (int)Math.Ceiling((double)TotalItems / Math.Max(1, PageSize));

    public PagedResult()
    {
    }

    public PagedResult(int page, int pageSize, int totalItems, List<T> items)
    {
        Page = page;
        PageSize = pageSize;
        TotalItems = totalItems;
        Items = items;
    }
}