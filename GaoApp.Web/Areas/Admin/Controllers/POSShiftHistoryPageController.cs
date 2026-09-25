using Microsoft.AspNetCore.Authorization;
using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos-shift/history")]
[Authorize(Policy = PermissionCodes.Pos.Shift.View)]
public class POSShiftHistoryPageController : BasePOSPageController
{
    public POSShiftHistoryPageController(
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
        : base(runtimeContext, posShiftService)
    {
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await BindPOSHeaderContextAsync(ct);
        ViewData["Title"] = "Lịch sử ca POS";
        return View();
    }
}