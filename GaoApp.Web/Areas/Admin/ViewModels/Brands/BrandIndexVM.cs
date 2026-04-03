using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Brands;

namespace GaoApp.Web.Areas.Admin.ViewModels.Brands;

public sealed class BrandIndexVM
{
    public string? SearchString { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public PagedResult<BrandListItemDto> Paged { get; set; } = new();
}
