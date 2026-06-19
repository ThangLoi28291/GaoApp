
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.Areas.Admin.ViewModels.Invoices;

public class InvoiceIntegrationLogCleanupViewModel
{
    public InvoiceIntegrationLogCleanupRequestDto Request { get; set; } = new();

    public InvoiceIntegrationLogCleanupResultDto? Result { get; set; }
}
