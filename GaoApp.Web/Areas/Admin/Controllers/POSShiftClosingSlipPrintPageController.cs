using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Pos.Shift.View)]
[Route("admin/pos-shift/closing-slip-print")]
public class POSShiftClosingSlipPrintPageController : Controller
{
    [HttpGet("")]
    public IActionResult Index([FromQuery] int? id, [FromQuery] int? shiftId)
    {
        if ((!id.HasValue || id <= 0) && (!shiftId.HasValue || shiftId <= 0))
            return BadRequest("Phiếu bàn giao không hợp lệ.");

        ViewBag.SlipId = id;
        ViewBag.ShiftId = shiftId;

        return View();
    }
}