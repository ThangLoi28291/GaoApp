

using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Web.Areas.Admin.ViewModels.Invoices;
public class InvoiceDashboardIndexViewModel
{
    public InvoiceDashboardQueryDto Query { get; set; } = new();

    public InvoiceDashboardDto? Dashboard { get; set; }
}