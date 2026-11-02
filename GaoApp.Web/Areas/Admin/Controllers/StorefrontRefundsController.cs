using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/storefront/refunds"), Authorize(Policy = StorefrontPermissions.ViewOrders)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[StorefrontCommandExceptionFilter, RequestSizeLimit(16384)]
public sealed class StorefrontRefundsController(IStorefrontCancellationService cancellation, IStorefrontRefundService refunds,
    AppDbContext db, ICurrentUser user, ICurrentStorePermissionService permissions) : BaseAdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if (!ModelState.IsValid || context.ActionArguments.Any(x => x.Key == "request" && x.Value is null))
            context.Result = BadRequest(new { message = "Thông tin chưa hợp lệ. Kiểm tra các trường; nếu lần gửi trước chưa rõ kết quả, giữ đúng mã yêu cầu cũ." });
        base.OnActionExecuting(context);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] StorefrontOrderControlQuery query, CancellationToken ct)
    {
        ViewData["Query"] = query;
        return View(await cancellation.ListAsync(query, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var control = await cancellation.GetAsync(id, ct);
        var rows = await refunds.GetAsync(id, ct);
        var store = db.CurrentStoreId is > 0 && db.CurrentStoreId == CurrentStoreId ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Phạm vi cửa hàng không hợp lệ.");
        var actor = user.UserId is > 0 ? user.UserId.Value : throw new ForbiddenAppException("Cần đăng nhập.");
        var canApprove = await permissions.HasPermissionAsync(store, actor, StorefrontPermissions.ApproveRefund, ct);
        var canConfirm = await permissions.HasPermissionAsync(store, actor, StorefrontPermissions.ConfirmRefund, ct) &&
            await permissions.HasPermissionAsync(store, actor, PermissionCodes.Report.Treasury.Manage, ct);
        var owners = canConfirm ? await db.LegalEntities.AsNoTracking().Where(x => x.StoreId == store && x.IsActive)
            .OrderBy(x => x.Name).Take(1000).Select(x => new StorefrontOption(x.Id, x.Name)).ToListAsync(ct) : [];
        var accounts = canConfirm ? await db.StoreBankAccounts.AsNoTracking().Where(x => x.StoreId == store && x.IsActive)
            .OrderBy(x => x.BankName).ThenBy(x => x.Id).Take(1000).Select(x => new { x.Id, x.BankName, x.AccountName, x.AccountNumber, x.RowVersion }).ToListAsync(ct) : [];
        return View(new StorefrontRefundPage(control, rows, owners,
            accounts.Where(x => x.RowVersion is { Length: 8 }).Select(x => new StorefrontPayerBankOption(x.Id, x.BankName + " · " + x.AccountName + " · …" + x.AccountNumber[^Math.Min(4, x.AccountNumber.Length)..], Convert.ToBase64String(x.RowVersion))).ToList(),
            canApprove, canConfirm, store, actor));
    }

    [HttpGet("{id:int}/state")]
    public async Task<IActionResult> State(int id, CancellationToken ct)
        => Json(new { cancellation = await cancellation.GetAsync(id, ct), refunds = await refunds.GetAsync(id, ct) });

    [HttpGet("{id:int}/payout-sources"), Authorize(Policy = StorefrontPermissions.ConfirmRefund), Authorize(Policy = PermissionCodes.Report.Treasury.Manage)]
    public async Task<IActionResult> Sources(int id, int refundId, CancellationToken ct)
        => Json(await refunds.PayoutSourcesAsync(id, refundId, ct));

    [HttpPost("{id:int}/cancellation/approve"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ApproveCancellation)]
    [Consumes("application/json")]
    public async Task<IActionResult> ApproveCancellation(int id, [FromBody] StorefrontCancellationDecisionRequest request, CancellationToken ct)
        => Json(await cancellation.ApproveAsync(id, request, ct));

    [HttpPost("{id:int}/cancellation/reject"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ApproveCancellation)]
    [Consumes("application/json")]
    public async Task<IActionResult> RejectCancellation(int id, [FromBody] StorefrontCancellationDecisionRequest request, CancellationToken ct)
        => Json(await cancellation.RejectAsync(id, request, ct));

    [HttpPost("{id:int}/cancellation/acknowledge-delivery"), ValidateAntiForgeryToken]
    [Authorize(Policy = StorefrontPermissions.ApproveCancellation), Authorize(Policy = StorefrontPermissions.Reconcile)]
    [Consumes("application/json")]
    public async Task<IActionResult> AcknowledgeDelivery(int id, [FromBody] StorefrontCancellationDecisionRequest request, CancellationToken ct)
        => Json(await cancellation.AcknowledgeDeliveryConflictAsync(id, request, ct));

    [HttpPost("{id:int}/approve"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ApproveRefund)]
    [Consumes("application/json")]
    public async Task<IActionResult> ApproveRefund(int id, [FromBody] StorefrontRefundDecisionRequest request, CancellationToken ct)
        => Json(await refunds.ApproveAsync(id, request, ct));

    [HttpPost("{id:int}/reject"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ApproveRefund)]
    [Consumes("application/json")]
    public async Task<IActionResult> RejectRefund(int id, [FromBody] StorefrontRefundDecisionRequest request, CancellationToken ct)
        => Json(await refunds.RejectAsync(id, request, ct));

    [HttpPost("{id:int}/confirm-execution"), ValidateAntiForgeryToken]
    [Authorize(Policy = StorefrontPermissions.ConfirmRefund), Authorize(Policy = PermissionCodes.Report.Treasury.Manage)]
    [Consumes("application/json")]
    public async Task<IActionResult> ConfirmExecution(int id, [FromBody] StorefrontRefundExecutionRequest request, CancellationToken ct)
        => Json(await refunds.ConfirmExecutionAsync(id, request, ct));
}
