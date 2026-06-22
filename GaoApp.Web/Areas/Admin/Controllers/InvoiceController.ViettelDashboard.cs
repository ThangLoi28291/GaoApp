using GaoApp.Application.DTOs.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public partial class InvoiceController
{
    [HttpGet]
    public async Task<IActionResult> ViettelDashboard(
        [FromQuery] ViettelInvoiceDashboardQueryDto query,
        CancellationToken ct)
    {
        var result = await _viettelDashboardService.GetDashboardAsync(
            query,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                result.Error?.Message,
                "Không tải được dashboard Viettel.");

            return RedirectToAction(nameof(Index));
        }

        return View("ViettelDashboard", result.Value);
    }
}