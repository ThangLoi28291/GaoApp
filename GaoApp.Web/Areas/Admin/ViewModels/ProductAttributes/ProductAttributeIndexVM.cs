using GaoApp.Application.Common;
using GaoApp.Application.DTOs.ProductAttributes;

namespace GaoApp.Web.Areas.Admin.ViewModels.ProductAttributes;

public sealed class ProductAttributeIndexVM
{
    public string? SearchString { get; set; }
    public bool? Status { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int TotalAttributeCount { get; set; }
    public int ActiveAttributeCount { get; set; }
    public int InactiveAttributeCount { get; set; }

    public PagedResult<ProductAttributeListItemDto> Paged { get; set; } = new();
}
