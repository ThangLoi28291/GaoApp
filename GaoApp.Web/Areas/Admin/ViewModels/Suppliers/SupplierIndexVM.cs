using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Suppliers;

namespace GaoApp.Web.Areas.Admin.ViewModels.Suppliers;

public sealed class SupplierIndexVM
{
    public string? SearchString { get; set; } = "";
    public bool? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int TotalSupplierCount { get; set; }
    public int ActiveSupplierCount { get; set; }
    public int InactiveSupplierCount { get; set; }

    public PagedResult<SupplierListItemDto> Paged { get; set; } = new();
}
