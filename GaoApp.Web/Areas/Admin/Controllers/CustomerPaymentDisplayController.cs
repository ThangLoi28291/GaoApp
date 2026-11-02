using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/customer-display/payment-state")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerPaymentDisplayController(ICurrentPOSContext pos, CustomerPaymentDisplayService display) : Controller
{
    [HttpGet]
    public async Task<IActionResult> State(CancellationToken ct)
    {
        if (!pos.IsAvailable) return Conflict(new { message = "Chưa xác định quầy thanh toán." });
        return Ok(await display.GetAsync(pos.StoreId, pos.TerminalId, ct));
    }
}
