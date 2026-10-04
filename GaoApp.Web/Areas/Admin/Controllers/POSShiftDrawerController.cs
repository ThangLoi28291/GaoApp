using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[ApiController]
[Route("admin/pos/shift/cash-drawer")]
[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class POSShiftDrawerController(
    IPOSShiftService shifts, ICurrentStore store, ICurrentUser user,
    IPOSAuditLogRepository audit) : ControllerBase
{
    public sealed class CloseDrawerRequest
    {
        [Range(1, int.MaxValue)]
        public int ShiftId { get; set; }
    }

    [HttpPost("receive")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Open)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        if (!HasContext()) return Forbid();
        // Checks the paired terminal and rejects another cashier's open shift too.
        if (await shifts.GetCurrentOpenAsync(ct) != null)
            return Conflict(new { message = "Quầy đã có ca đang mở. Vui lòng tải lại ca POS." });

        return await RecordAsync("Mở két nhận ca", "Receive", null, null, ct);
    }

    [HttpPost("close")]
    [Authorize(Policy = PermissionCodes.Pos.Shift.Close)]
    public async Task<IActionResult> Close([FromBody] CloseDrawerRequest request, CancellationToken ct)
    {
        if (!HasContext()) return Forbid();
        var shift = await shifts.GetCurrentOpenAsync(ct);
        if (shift == null || shift.Status != POSShiftStatus.Open || shift.Id != request.ShiftId)
            return Conflict(new { message = "Ca đã thay đổi hoặc chưa mở. Vui lòng tải lại ca POS." });

        return await RecordAsync("Mở két chốt ca", "Close", shift.Id, shift.ShiftCode, ct);
    }

    private bool HasContext() => store.StoreId > 0 && user.IsAuthenticated
        && user.UserId is > 0 && user.TerminalId is > 0;

    private async Task<IActionResult> RecordAsync(string reason, string purpose, int? shiftId, string? shiftCode, CancellationToken ct)
    {
        var entry = new POSAuditLog
        {
            StoreId = store.StoreId, UserId = user.UserId,
            Action = "CASH_DRAWER_OPEN_REQUESTED", Note = reason, CreatedAtUtc = DateTime.UtcNow,
            MetadataJson = JsonSerializer.Serialize(new
            {
                ShiftId = shiftId, ShiftCode = shiftCode, TerminalId = user.TerminalId,
                Reason = reason, Purpose = purpose, Source = "POSShift", Outcome = "Requested"
            })
        };
        await audit.AddAsync(entry, ct);
        await audit.SaveChangesAsync(ct);
        // A receiving count precedes creation of the new shift: its audit belongs to the terminal/user.
        return Ok(new { auditId = entry.Id, shiftId, purpose });
    }
}
