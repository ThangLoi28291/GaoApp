

using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.Areas.Admin.ViewModels.Invoices;

public class InvoiceCorrectionCreateViewModel
{
    public InvoiceCorrectionCreateInfoDto Info { get; set; } = new();

    public CreateInvoiceCorrectionRequestDto Request { get; set; } = new();
}