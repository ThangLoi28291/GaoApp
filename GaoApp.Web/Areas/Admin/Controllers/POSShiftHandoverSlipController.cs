using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.POSShiftHandoverSlips;
using GaoApp.Application.Interfaces.Services.POSShiftHandoverSlips;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[ApiController]
[Route("admin/pos/shift-handover-slips")]
[Authorize]
public class POSShiftHandoverSlipController : ControllerBase
{
    private readonly IPOSShiftHandoverSlipService _service;

    public POSShiftHandoverSlipController(IPOSShiftHandoverSlipService service)
    {
        _service = service;
    }

    [HttpPost("")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Open)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePOSShiftHandoverSlipRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);

        if (result == null)
            return NotFound(new { message = "Không tìm thấy phiếu nhận ca." });

        return Ok(result);
    }

    [HttpGet("barcode")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> GetByBarcode(
        [FromQuery] string barcodeValue,
        CancellationToken ct)
    {
        var result = await _service.GetByBarcodeAsync(barcodeValue, ct);

        if (result == null)
            return NotFound(new { message = "Không tìm thấy phiếu nhận ca theo mã quét." });

        return Ok(result);
    }

    [HttpGet("")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.View)]
    public async Task<IActionResult> Query(
        [FromQuery] POSShiftHandoverSlipStatus? status,
        [FromQuery] int? terminalId,
        [FromQuery] int? warehouseId,
        [FromQuery] int? assignedToUserId,
        [FromQuery] string? keyword,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtcExclusive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var req = new QueryPOSShiftHandoverSlipRequest
        {
            Status = status,
            TerminalId = terminalId,
            WarehouseId = warehouseId,
            AssignedToUserId = assignedToUserId,
            Keyword = keyword,
            FromUtc = fromUtc,
            ToUtcExclusive = toUtcExclusive,
            Page = page,
            PageSize = pageSize
        };

        var result = await _service.QueryAsync(req, ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/mark-printed")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Open)]
    public async Task<IActionResult> MarkPrinted(int id, CancellationToken ct)
    {
        var result = await _service.MarkPrintedAsync(id, ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Open)]
    public async Task<IActionResult> Cancel(
        int id,
        [FromBody] CancelPOSShiftHandoverSlipRequest request,
        CancellationToken ct)
    {
        var result = await _service.CancelAsync(id, request, ct);
        return Ok(result);
    }
}