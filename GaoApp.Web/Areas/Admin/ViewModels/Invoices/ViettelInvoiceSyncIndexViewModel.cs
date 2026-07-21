
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.Areas.Admin.ViewModels.Invoices;

public class ViettelInvoiceSyncIndexViewModel
{
    public ViettelInvoiceListSyncRequestDto Query { get; set; } = new();

    public ViettelInvoiceListSyncResultDto? Result { get; set; }
}
