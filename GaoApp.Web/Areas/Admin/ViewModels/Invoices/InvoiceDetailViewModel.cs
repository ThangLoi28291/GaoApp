using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.Areas.Admin.ViewModels.Invoices;

public class InvoiceDetailViewModel
{
    public InvoiceHeadDto Invoice { get; set; } = new();
}