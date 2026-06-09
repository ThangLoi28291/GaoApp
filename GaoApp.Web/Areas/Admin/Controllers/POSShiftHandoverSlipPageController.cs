using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Pos.Shift.View)]
[Route("admin/pos-shift/handover-slips")]
public class POSShiftHandoverSlipPageController : BasePOSPageController
{
    public POSShiftHandoverSlipPageController(
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
        : base(runtimeContext, posShiftService)
    {
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await BindPOSHeaderContextAsync(ct);

        ViewData["Title"] = "Phiếu nhận ca POS";

        return View();
    }
}