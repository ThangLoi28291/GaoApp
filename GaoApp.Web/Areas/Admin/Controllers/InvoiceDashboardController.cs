using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.System.Integration.View)]
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
        if (!ModelState.IsValid)
            return BadRequest(InvoiceReadLimits.InvalidQueryMessage);
        query.NormalizeAndValidate();

        var result = await _dashboardService.GetDashboardAsync(query, ct);

        if (!result.IsSuccess)
        {
            TempData["Error"] = InvoiceErrorReadSanitizer.UserMessage(result.Error) ?? "Không tải được dashboard hóa đơn.";

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
