using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Taxes;

namespace GaoApp.Web.Areas.Admin.ViewModels.Taxes;

public sealed class TaxIndexVM
{
    public string? SearchString { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public PagedResult<TaxListItemDto> Paged { get; set; } = new();
}
