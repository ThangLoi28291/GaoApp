using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionCodes.System.BankAccount.View)]
[Route("admin/bank-notifications")]
public sealed class BankNotificationsController(AppDbContext db, BankNotificationMonitor monitor, BankNotificationBridgeService bridge, IAcbOrderLockProvider locks) : BaseAdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await LoadViewStateAsync(ct));

    // Render the same authorized, store-scoped rows as the initial page. This is
    // deliberately HTML rather than serializing device entities (which hold keys).
    [HttpGet("live-state")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LiveState(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return PartialView("_LiveState", await LoadViewStateAsync(ct));
    }

    private async Task<IReadOnlyList<BankNotificationDevice>> LoadViewStateAsync(CancellationToken ct)
    {
        ViewBag.Accounts = await db.StoreBankAccounts.AsNoTracking().Where(x => x.StoreId == CurrentStoreId && x.IsActive && x.BankCode == "ACB" &&
            x.ConfirmMode == BankQrConfirmMode.Manual && x.QrRenderMode == BankQrRenderMode.LocalEmvQr).ToListAsync(ct);
        ViewBag.Waiting = await bridge.AdminWaitingAsync(ct);
        // Read decisions after the waiting list so a concurrent approval is
        // represented by the newest history state in this display snapshot.
        ViewBag.History = await bridge.AdminHistoryAsync(ct);
        var devices = await db.Set<BankNotificationDevice>().AsNoTracking().Where(x => x.StoreId == CurrentStoreId).OrderByDescending(x => x.Id).ToListAsync(ct);
        ViewBag.PhoneStates = devices.ToDictionary(x => x.Id, monitor.State);
        return devices;
    }
    [HttpGet("history-state")]
    public async Task<IActionResult> HistoryState(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var history = await bridge.AdminHistoryAsync(ct);
        return Ok(new { pendingQrIds = history.Where(x => x.AwaitingTerminal && x.QrId.HasValue).Select(x => x.QrId!.Value).Distinct().Order().ToArray() });
    }
    [HttpPost("devices")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.BankAccount.Manage)]
    public async Task<IActionResult> CreateDevice(int bankAccountId, string name, CancellationToken ct)
    {
        if (!Request.IsHttps) return BadRequest("Mở trang HTTPS của cửa hàng để tạo cấu hình kết nối điện thoại.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) return BadRequest("Nhập tên điện thoại, tối đa 120 ký tự.");
        if (!await db.StoreBankAccounts.AnyAsync(x => x.StoreId == CurrentStoreId && x.Id == bankAccountId && x.IsActive && x.BankCode == "ACB" &&
            x.ConfirmMode == BankQrConfirmMode.Manual && x.QrRenderMode == BankQrRenderMode.LocalEmvQr, ct)) return BadRequest("Chọn tài khoản ACB dùng QR thủ công của cửa hàng này.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var bank = await db.StoreBankAccounts.AsNoTracking().SingleAsync(x => x.StoreId == CurrentStoreId && x.Id == bankAccountId, ct);
        var device = new BankNotificationDevice { StoreId = CurrentStoreId, BankAccountId = bankAccountId, BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank), Name = name.Trim(), TokenHash = BankNotificationParser.Hash(token) };
        db.Add(device); await db.SaveChangesAsync(ct);
        var config = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, apiUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/bank-notifications",
            deviceId = device.Id, token, deviceName = device.Name });
        Response.Headers.CacheControl = "no-store";
        return File(config, "application/json", $"gaoapp-notification-device-{device.Id}.json");
    }
    [HttpPost("devices/{id:int}/revoke")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.BankAccount.Manage)]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        var device = await db.Set<BankNotificationDevice>().SingleOrDefaultAsync(x => x.StoreId == CurrentStoreId && x.Id == id, ct);
        if (device == null) return NotFound();
        device.Enabled = false; await db.SaveChangesAsync(ct); return RedirectToAction(nameof(Index));
    }
    [HttpPost("devices/{id:int}/renew")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.BankAccount.Manage)]
    public async Task<IActionResult> Renew(int id, CancellationToken ct)
    {
        if (!Request.IsHttps) return BadRequest("Mở trang HTTPS để cấp lại cấu hình.");
        await using var gate = await locks.AcquireAsync(db, 0, ct);
        var device = await db.Set<BankNotificationDevice>().SingleOrDefaultAsync(x => x.StoreId == CurrentStoreId && x.Id == id, ct);
        if (device == null) return NotFound();
        var bank = await db.StoreBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == CurrentStoreId && x.Id == device.BankAccountId && x.IsActive &&
            x.BankCode == "ACB" && x.ConfirmMode == BankQrConfirmMode.Manual && x.QrRenderMode == BankQrRenderMode.LocalEmvQr, ct);
        if (bank == null || BankNotificationParser.AccountFingerprint(bank) != device.BankAccountFingerprint)
            return Conflict("Tài khoản nhận đã thay đổi. Đối soát hàng đợi cũ trước khi đăng ký cấu hình khác.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        device.TokenHash = BankNotificationParser.Hash(token); device.Enabled = true;
        await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return File(JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1,
            apiUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/api/bank-notifications", deviceId = device.Id, token, deviceName = device.Name }),
            "application/json", $"gaoapp-notification-device-{device.Id}.json");
    }
    public sealed record AdminApproval(int QrId, decimal Amount, int? ReceiptId = null);
    [HttpPost("approve")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.BankAccount.Manage)]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> Approve([FromBody] AdminApproval decision, CancellationToken ct)
    {
        try { return Ok(await bridge.AdminApproveAsync(decision.QrId, decision.Amount, decision.ReceiptId, ct)); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}

[Area("Admin")]
[Authorize(Policy = PermissionCodes.Pos.Payment.Create)]
[Route("admin/pos/bank-notifications")]
public sealed class PosBankNotificationsController(BankNotificationBridgeService service) : Controller
{
    public sealed record Decision(int ReceiptId, int WaitId, bool Automatic = false);
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct) => Ok(await service.PendingAsync(ct));
    [HttpGet("admin-approvals")]
    public async Task<IActionResult> AdminApprovals(CancellationToken ct) => Ok(await service.AdminApprovalsAsync(ct));
    [HttpPost("complete-admin/{qrId:int}")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> CompleteAdmin(int qrId, CancellationToken ct)
    {
        try { return Ok(await service.CompleteAdminApprovalAsync(qrId, ct)); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
    [HttpGet("status/{qrId:int}")]
    public async Task<IActionResult> Status(int qrId, CancellationToken ct)
    {
        try { return Ok(await service.WaitingStatusAsync(qrId, ct)); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
    [HttpPost("manual-review/{qrId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ManualReview(int qrId, CancellationToken ct)
    {
        try { return Ok(await service.BeginManualReviewAsync(qrId, ct)); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
    [HttpPost("arm/{qrId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Arm(int qrId, CancellationToken ct)
    {
        try { var enabled = await service.ArmAsync(qrId, ct); return Ok(new { success = true, enabled }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
    [HttpPost("confirm")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Pos.Order.Finalize)]
    public async Task<IActionResult> Confirm([FromBody] Decision decision, CancellationToken ct)
    {
        try { return Ok(await service.ConfirmAsync(decision.ReceiptId, decision.WaitId, decision.Automatic, ct)); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
    [HttpPost("dismiss")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss([FromBody] Decision decision, CancellationToken ct)
    {
        try { await service.DismissAsync(decision.ReceiptId, decision.WaitId, ct); return Ok(new { success = true }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
