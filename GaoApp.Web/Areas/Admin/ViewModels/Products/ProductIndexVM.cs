using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Web.Areas.Admin.ViewModels.Products;

public class ProductIndexVM
{
    public string? SearchString { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public PagedResult<ProductListItemDto> Paged { get; set; } = new();
}
