using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Acb;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/acb/reconciliation")]
[Authorize(Policy = PermissionCodes.Pos.Payment.View)]
public sealed class AcbReconciliationController(AcbReconciliationService reports, AcbCallbackInbox inbox,
    IAuthorizationService authorization, AcbCallbackHealthService? health = null,
    AcbCallbackRoutingService? routing = null, AcbBankLookupService? bankLookup = null) : BaseAdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? source, int page = 1, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return BadRequest("Ngày hoặc số trang không hợp lệ.");
        try
        {
            ViewBag.CanRetry = (await authorization.AuthorizeAsync(User, PermissionCodes.System.Integration.Manage)).Succeeded;
            if (health != null)
                ViewBag.CallbackHealth = await ReadHealthAsync(ct);
            return View(await reports.ReadAsync(from, to, source, page, ct));
        }
        catch (ArgumentException error) { return BadRequest(error.Message); }
    }

    [HttpGet("callback-status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CallbackStatus(CancellationToken ct)
    {
        if (health == null) return NotFound();
        ViewBag.CanRetry = (await authorization.AuthorizeAsync(User, PermissionCodes.System.Integration.Manage)).Succeeded;
        return PartialView("_AcbCallbackHealth", await ReadHealthAsync(ct));
    }

    [HttpPost("bank-lookup"), ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> BankLookup(DateTime? from, DateTime? to, int page = 1, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return Problem(statusCode: 400, detail: "Ngày hoặc số trang không hợp lệ.");
        if (bankLookup == null) return Problem(statusCode: 503, detail: "Chức năng tra cứu ACB chưa sẵn sàng.");
        try
        {
            var result = await bankLookup.ReadAsync(from, to, page, ct);
            Response.Headers["X-Acb-Data-Source"] = "bank-lookup";
            return PartialView("_AcbBankLookup", result);
        }
        catch (ArgumentException error) { return Problem(statusCode: 400, detail: error.Message); }
        catch (AcbApiException error) { return Problem(statusCode: 502, detail: error.Message); }
        catch (InvalidOperationException error) { return Problem(statusCode: 400, detail: error.Message); }
        catch (JsonException) { return Problem(statusCode: 502, detail: "ACB trả dữ liệu không hợp lệ. Hãy thử tra cứu lại."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Problem(statusCode: 504, detail: "Tra cứu ACB quá thời gian chờ. Hãy thử lại."); }
    }

    private async Task<AcbCallbackHealth> ReadHealthAsync(CancellationToken ct)
    {
        var canManage = ViewBag.CanRetry is true && routing != null && await routing.CanManageAsync(User, ct);
        return await health!.ReadAsync(CurrentStoreId, $"{Request.Scheme}://{Request.Host}/Admin/api-callback", canManage, ct);
    }

    [HttpPost("{receiptId:int}/retry"), ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.System.Integration.Manage)]
    public async Task<IActionResult> Retry(int receiptId, DateTime? from, DateTime? to, string? source, int page, CancellationToken ct)
    {
        await inbox.RetryAsync(receiptId, ct);
        TempData["AcbRetry"] = $"Đã đưa trang thông báo #{receiptId} vào hàng đợi xử lý lại. Bấm Xem đối soát để cập nhật kết quả.";
        return RedirectToAction(nameof(Index), new { from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd"), source, page });
    }
}
