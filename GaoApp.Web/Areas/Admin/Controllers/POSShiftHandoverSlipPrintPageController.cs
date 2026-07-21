using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GaoApp.Application.Common.Security;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Pos.Shift.View)]
[Route("admin/pos-shift/handover-slip-print")]

public class POSShiftHandoverSlipPrintPageController : Controller
{
    [HttpGet("")]
    public IActionResult Index([FromQuery] int id)
    {
        if (id <= 0)
            return BadRequest("Phiếu nhận ca không hợp lệ.");

        ViewBag.SlipId = id;
        return View();
    }
}