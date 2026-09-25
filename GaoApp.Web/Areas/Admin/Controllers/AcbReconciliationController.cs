using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/acb/reconciliation")]
[Authorize(Policy = PermissionCodes.Pos.Payment.View)]
public sealed class AcbReconciliationController(AcbReconciliationService reports, AcbCallbackInbox inbox,
    IAuthorizationService authorization) : BaseAdminController
{
    [HttpGet]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? source, int page = 1, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return BadRequest("Ngày hoặc số trang không hợp lệ.");
        try
        {
            ViewBag.CanRetry = (await authorization.AuthorizeAsync(User, PermissionCodes.System.Integration.Manage)).Succeeded;
            return View(await reports.ReadAsync(from, to, source, page, ct));
        }
        catch (ArgumentException error) { return BadRequest(error.Message); }
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
