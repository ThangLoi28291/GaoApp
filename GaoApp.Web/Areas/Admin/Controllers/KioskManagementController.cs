using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Kiosk;
using GaoApp.Web.Services.Acb;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Application.DTOs.POSShifts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Route("admin/kiosks"), Authorize(Roles = "ADMIN"), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KioskManagementController(AppDbContext db, KioskAccess access, KioskService kiosk,
    AcbPaymentService bank, IPOSService pos, IPOSShiftService shifts) : Controller
{
    private int Store => db.CurrentStoreId ?? 0;
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await db.Set<KioskStation>().AsNoTracking()
        .Where(x => x.StoreId == Store && !x.IsDeleted).Select(x => new {
            x.Id, x.IsActive, x.IsPaused, activated = x.DeviceHash != null, x.LastSeenAtUtc, x.HelpRequestedAtUtc, x.OrderId,
            total = db.Orders.Where(o => o.StoreId == Store && o.Id == x.OrderId).Select(o => (decimal?)o.GrandTotal).FirstOrDefault(),
            paymentStatus = db.Set<AcbQrSession>().Where(q => q.StoreId == Store && q.OrderId == x.OrderId && q.TerminalId == x.TerminalId)
                .OrderByDescending(q => q.Id).Select(q => (int?)q.Status).FirstOrDefault()
        }).ToListAsync(ct));
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Terminals = await db.POSTerminals.Where(x => x.StoreId == Store && x.IsActive && !x.IsDeleted && !db.Set<KioskStation>().Any(k => k.StoreId == Store && k.TerminalId == x.Id)).OrderBy(x => x.Name).ToListAsync(ct);
        ViewBag.Warehouses = await db.Warehouses.Where(x => x.StoreId == Store && x.IsActive && !x.IsDeleted).OrderBy(x => x.Name).ToListAsync(ct);
        return View(await db.Set<KioskStation>().AsNoTracking().Include(x => x.Terminal).Where(x => x.StoreId == Store && !x.IsDeleted).OrderBy(x => x.Id).ToListAsync(ct));
    }
    [HttpPost("create")]
    public async Task<IActionResult> Create(KioskCreateRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid || Store <= 0) return BadRequest();
        var terminal = await db.POSTerminals.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.TerminalId && x.IsActive && !x.IsDeleted, ct);
        if (terminal == null || !await db.Warehouses.AnyAsync(x => x.StoreId == Store && x.Id == request.WarehouseId && x.IsActive && !x.IsDeleted, ct)) return BadRequest("Quầy hoặc kho không hợp lệ.");
        await using var gate = await KioskLock.AcquireAsync(db, -terminal.Id, ct);
        if (await db.Set<KioskStation>().AnyAsync(x => x.StoreId == Store && x.TerminalId == terminal.Id, ct) ||
            await db.POSShifts.AnyAsync(x => x.StoreId == Store && x.TerminalId == terminal.Id && x.Status == POSShiftStatus.Open, ct))
            return BadRequest("Quầy đã cấu hình hoặc đang mở ca. Hãy tạo một quầy riêng cho máy tự phục vụ.");
        var key = KioskAccess.Secret();
        var station = new KioskStation { StoreId = Store, TerminalId = terminal.Id, WarehouseId = request.WarehouseId,
            ActivationHash = KioskAccess.Hash(key), ActivationExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            SystemUser = new User { UserName = "kiosk_" + Guid.NewGuid().ToString("N"), FullName = terminal.Name,
                IsActive = false, PasswordHash = "!KIOSK-NO-LOGIN!" } };
        db.Add(station); await db.SaveChangesAsync(ct);
        ViewBag.Key = key; ViewBag.TerminalName = terminal.Name; return View("Activation");
    }
    [HttpPost("{id:int}/{actionName}")]
    public async Task<IActionResult> Action(int id, string actionName, CancellationToken ct)
    {
        await using var gate = await KioskLock.AcquireAsync(db, id, ct);
        var station = await db.Set<KioskStation>().Include(x => x.Terminal).SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == id && !x.IsDeleted, ct);
        if (station == null) return NotFound();
        try
        {
        switch (actionName)
        {
            case "key":
                var key = KioskAccess.Secret(); station.ActivationHash = KioskAccess.Hash(key);
                station.ActivationExpiresAtUtc = DateTime.UtcNow.AddMinutes(30); station.IsActive = true;
                await db.SaveChangesAsync(ct); ViewBag.Key = key; ViewBag.TerminalName = station.Terminal.Name; return View("Activation");
            case "pause": station.IsPaused = !station.IsPaused; break;
            case "revoke": station.IsActive = false; station.DeviceHash = null; station.ActivationHash = null; station.CustomerId = null; break;
            case "help-done": station.HelpRequestedAtUtc = null; break;
            case "check":
                using (access.Enter(station)) await kiosk.PollAsync(station, ct, refreshBank: true); break;
            case "cancel-payment":
                using (access.Enter(station)) {
                    var pending = await db.Set<AcbQrSession>().Where(x => x.StoreId == Store && x.OrderId == station.OrderId && x.TerminalId == station.TerminalId && x.Status != AcbSessionStatus.Cancelled).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
                    if (pending != null && !await bank.CancelAsync(pending.QrRequestId, ct)) return BadRequest("Chưa thể hủy QR. Vui lòng kiểm tra giao dịch ngân hàng.");
                    if (station.OrderId is int orderId) await pos.CancelAsync(orderId, "Admin hủy phiên tự phục vụ", ct);
                    KioskService.Reset(station);
                } break;
            case "close-shift":
                if (station.OrderId != null) return BadRequest("Cần xử lý xong giao dịch của quầy trước khi chốt phiên.");
                using (access.Enter(station)) { await shifts.CloseAsync(new CloseShiftRequest { ClosingCashActual = 0, Note = "Chốt phiên quầy tự phục vụ" }, ct); }
                station.IsPaused = true; break;
            default: return BadRequest();
        }
        await db.SaveChangesAsync(ct); return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException error) { return BadRequest(new { message = error.Message }); }
    }
}
