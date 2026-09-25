using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Web.Areas.Admin.ViewModels.Products;

public sealed class ProductIndexVM
{
    public string? SearchString { get; set; }
    public int? CategoryId { get; set; }
    public string? Lifecycle { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int TotalProductCount { get; set; }
    public int PosAllowedProductCount { get; set; }
    public int NotForPosProductCount { get; set; }
    public int InactiveProductCount { get; set; }

    public IReadOnlyList<ProductCategoryFilterOptionVM> CategoryOptions { get; set; }
        = Array.Empty<ProductCategoryFilterOptionVM>();

    public PagedResult<ProductListItemDto> Paged { get; set; } = new();
}

public sealed class ProductCategoryFilterOptionVM
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
