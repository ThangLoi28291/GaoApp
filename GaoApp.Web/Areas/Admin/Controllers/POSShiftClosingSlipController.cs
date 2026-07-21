using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.POSShiftClosingSlips;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[ApiController]
[Route("admin/pos/shift-closing-slips")]
[Authorize]
public class POSShiftClosingSlipController : ControllerBase
{
    private readonly IPOSShiftClosingSlipService _service;

    public POSShiftClosingSlipController(IPOSShiftClosingSlipService service)
    {
        _service = service;
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);

        if (result == null)
            return NotFound(new { message = "Không tìm thấy phiếu bàn giao cuối ca." });

        return Ok(result);
    }

    [HttpGet("by-shift/{shiftId:int}")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetByShiftId(int shiftId, CancellationToken ct)
    {
        var result = await _service.GetByShiftIdAsync(shiftId, ct);

        if (result == null)
            return NotFound(new { message = "Không tìm thấy phiếu bàn giao của ca này." });

        return Ok(result);
    }

    [HttpPost("create-from-shift/{shiftId:int}")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Close)]
    public async Task<IActionResult> CreateFromShift(int shiftId, CancellationToken ct)
    {
        var result = await _service.CreateFromClosedShiftAsync(shiftId, ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/mark-printed")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> MarkPrinted(int id, CancellationToken ct)
    {
        var result = await _service.MarkPrintedAsync(id, ct);
        return Ok(result);
    }
}