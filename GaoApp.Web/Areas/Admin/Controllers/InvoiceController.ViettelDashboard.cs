using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
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
        if (!ModelState.IsValid)
            return BadRequest(InvoiceReadLimits.InvalidQueryMessage);
        query.NormalizeAndValidate();

        var result = await _viettelDashboardService.GetDashboardAsync(
            query,
            ct);

        if (!result.IsSuccess)
        {
            FlashError(
                InvoiceErrorReadSanitizer.UserMessage(result.Error),
                "Không tải được dashboard Viettel.");

            return RedirectToAction(nameof(Index));
        }

        return View("ViettelDashboard", result.Value);
    }
}