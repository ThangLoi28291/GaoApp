using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.POSShifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = PermissionCodes.Pos.Shift.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/pos-shift/reconciliation")]
public sealed class POSShiftReconciliationController(IPOSShiftReconciliationService service) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("data")]
    public async Task<IActionResult> Data(int? shiftId, CancellationToken ct)
        => Ok(await service.GetAsync(shiftId, ct));
}
