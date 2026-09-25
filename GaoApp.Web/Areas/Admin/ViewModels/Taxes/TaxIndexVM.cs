using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Taxes;

namespace GaoApp.Web.Areas.Admin.ViewModels.Taxes;

public sealed class TaxIndexVM
{
    public string? SearchString { get; set; }
    public bool? Status { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int TotalTaxCount { get; set; }
    public int ActiveTaxCount { get; set; }
    public int InactiveTaxCount { get; set; }

    public PagedResult<TaxListItemDto> Paged { get; set; } = new();
}
