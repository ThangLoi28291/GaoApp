using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Common.POS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[GaoApp.Web.Common.POS.StoreAdminOnly]
[Area("Admin")]
[Authorize(Policy = PermissionCodes.Pos.Shift.View)]
[Route("admin/pos-shift/manager-dashboard")]
public class POSShiftManagerDashboardPageController : BasePOSPageController
{
    public POSShiftManagerDashboardPageController(
        IPOSRuntimeContextAccessor runtimeContext,
        IPOSShiftService posShiftService)
        : base(runtimeContext, posShiftService)
    {
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await BindPOSHeaderContextAsync(ct);

        ViewData["Title"] = "Dashboard quản lý ca POS";

        return View();
    }
}