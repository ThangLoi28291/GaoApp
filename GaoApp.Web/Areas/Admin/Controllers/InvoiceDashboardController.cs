using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
public class InvoiceDashboardController : Controller
{
    private readonly IInvoiceDashboardService _dashboardService;

    public InvoiceDashboardController(
        IInvoiceDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] InvoiceDashboardQueryDto query,
        CancellationToken ct)
    {
        if (query.FromDate == default)
            query.FromDate = DateTime.Today;

        if (query.ToDate == default)
            query.ToDate = DateTime.Today;

        var result = await _dashboardService.GetDashboardAsync(query, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = result.Error?.Message ?? "Không tải được dashboard hóa đơn.";

            return View(new InvoiceDashboardIndexViewModel
            {
                Query = query
            });
        }

        return View(new InvoiceDashboardIndexViewModel
        {
            Query = query,
            Dashboard = result.Value
        });
    }
}