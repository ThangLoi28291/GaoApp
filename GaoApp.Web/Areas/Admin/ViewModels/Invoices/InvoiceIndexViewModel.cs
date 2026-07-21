using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.Areas.Admin.ViewModels.Invoices;

public class InvoiceIndexViewModel
{
    public InvoiceListQueryDto Query { get; set; } = new();

    public List<InvoiceListItemDto> Items { get; set; } = new();

    public int TotalItems { get; set; }

    public int TotalPages { get; set; }

    public int CurrentPage { get; set; }
}