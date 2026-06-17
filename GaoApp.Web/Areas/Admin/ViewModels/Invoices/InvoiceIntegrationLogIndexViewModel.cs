using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.Areas.Admin.ViewModels.Invoices;

public class InvoiceIntegrationLogIndexViewModel
{
    public InvoiceIntegrationLogQueryDto Query { get; set; } = new();

    public PagedResult<InvoiceIntegrationLogListItemDto> Result { get; set; } = new()
    {
        Items = new List<InvoiceIntegrationLogListItemDto>(),
        Page = 1,
        PageSize = 20,
        TotalItems = 0
    };

    public int TotalPages =>
        Result.PageSize <= 0
            ? 0
            : (int)Math.Ceiling(Result.TotalItems / (double)Result.PageSize);
}