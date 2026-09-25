using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Customers;

namespace GaoApp.Web.Areas.Admin.ViewModels.Customers;

public sealed class CustomerIndexVM
{
    public string SearchString { get; set; } = string.Empty;
    public string? PriceTier { get; set; }
    public bool? Status { get; set; }
    public bool? HaveDebt { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public CustomerManagementSummaryDto Summary { get; set; } = new();
    public PagedResult<CustomerListItemDto> Paged { get; set; } = new();
}
