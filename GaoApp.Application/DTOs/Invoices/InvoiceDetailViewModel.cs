using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.ViewModels.Invoices;

public class InvoiceDetailViewModel
{
    public InvoiceHeadDto Invoice { get; set; } = new();
}