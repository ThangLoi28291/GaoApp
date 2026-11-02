using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Route("admin/storefront/fulfillment"), Authorize(Policy = StorefrontPermissions.ViewOrders)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(2500000)]
[StorefrontCommandExceptionFilter]
public sealed class StorefrontFulfillmentController(IStorefrontFulfillmentService service, IStorefrontSettlementService settlement,
    IStorefrontOrderInboxService inbox, IStorefrontCodBankSourceService bankSources, AppDbContext db,
    ICurrentUser user, ICurrentStorePermissionService permissions) : BaseAdminController
{
    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if (!ModelState.IsValid || context.ActionArguments.Any(x => x.Key == "request" && x.Value is null))
            context.Result = BadRequest(new { message = "Dữ liệu thao tác không hợp lệ; kiểm tra các trường và giữ nguyên mã yêu cầu nếu lần gửi trước chưa rõ kết quả." });
        base.OnActionExecuting(context);
    }
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] StorefrontFulfillmentQuery query, CancellationToken ct)
    { if (!ModelState.IsValid) return BadRequest(new { message = "Bộ lọc không hợp lệ." }); ViewData["Query"] = query; return View(await service.ListAsync(query, ct)); }
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var detail = await service.GetAsync(id, ct); var snapshot = await inbox.GetAsync(id, ct); var money = await settlement.GetAsync(id, ct);
        var store = CurrentStoreId; var actor = user.UserId!.Value;
        async Task<bool> Can(string p) => await permissions.HasPermissionAsync(store, actor, p, ct);
        var canReconcile = await Can(StorefrontPermissions.Reconcile);
        var couriers = await db.UserInStores.AsNoTracking().Where(x => x.StoreId == store && x.IsActive && x.User.IsActive)
            .OrderBy(x => x.User.FullName ?? x.User.UserName).Take(1000).Select(x => new StorefrontOption(x.UserId, x.User.FullName ?? x.User.UserName)).ToListAsync(ct);
        var owners = await db.LegalEntities.AsNoTracking().Where(x => x.StoreId == store && x.IsActive).OrderBy(x => x.Name).Take(100).Select(x => new StorefrontOption(x.Id, x.Name)).ToListAsync(ct);
        var cash = canReconcile ? await db.POSShiftCashTransactions.AsNoTracking().Where(x => x.StoreId == store && x.Type == POSShiftCashTransactionType.CashIn && x.Amount > 0 &&
            x.CustomerDebtReceiptId == null && x.CustomerDepositEntryId == null && !db.Set<StorefrontCodReceipt>().Any(r => r.StoreId == store && r.POSShiftCashTransactionId == x.Id))
            .OrderByDescending(x => x.Id).Take(100).Select(x => new { x.Id, x.Amount, x.Reason }).ToListAsync(ct) : [];
        var expenses = canReconcile ? await db.OperatingExpenses.AsNoTracking().Where(x => x.StoreId == store && x.Status == "confirmed" && !x.IsPaid &&
            !db.TreasuryEntries.Any(t => t.StoreId == store && t.OperatingExpenseId == x.Id) && !db.Set<StorefrontCarrierWithholding>().Any(w => w.StoreId == store && w.OperatingExpenseId == x.Id))
            .OrderByDescending(x => x.Id).Take(100).Select(x => new { x.Id, x.Name, x.Amount }).ToListAsync(ct) : [];
        var notifications = canReconcile ? await (from a in db.Set<StorefrontNotificationAttestation>().AsNoTracking()
            join r in db.Set<BankNotificationReceipt>().AsNoTracking() on a.BankNotificationReceiptId equals r.Id
            where a.StoreId == store && r.StoreId == store && r.PaymentId == null && r.AppliedWaitId == null
            orderby a.Id descending select new { r.Id, r.TransactionCode, r.Amount }).Take(100).ToListAsync(ct) : [];
        static string Vnd(decimal value) => value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + " đ";
        return View(new StorefrontFulfillmentPage(detail, snapshot, money, await Can(StorefrontPermissions.Prepare), await Can(StorefrontPermissions.Dispatch),
            await Can(StorefrontPermissions.RecordDelivery), await Can(StorefrontPermissions.ReceiveReturns), canReconcile, await Can(StorefrontPermissions.PostSale), couriers, owners,
            cash.Select(x => new StorefrontOption(x.Id, Vnd(x.Amount) + " · " + x.Reason)).ToList(), expenses.Select(x => new StorefrontOption(x.Id, x.Name + " · " + Vnd(x.Amount))).ToList(),
            canReconcile ? await bankSources.ListAsync(ct) : [], notifications.Select(x => new StorefrontOption(x.Id, x.TransactionCode + " · " + Vnd(x.Amount))).ToList(),
            await db.Set<StorefrontCustodyIncident>().AsNoTracking().Where(x => x.StoreId == store && x.StorefrontOrderId == id && x.ReconciledByEvidenceId == null).Select(x => (int?)x.Id).SingleOrDefaultAsync(ct)));
    }
    [HttpPost("{id:int}/prepare"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Prepare)]
    public async Task<IActionResult> Prepare(int id, [FromBody] StorefrontOperationRequest request, CancellationToken ct) => Json(await service.StartPreparationAsync(id, request, ct));
    [HttpPost("{id:int}/pick"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Prepare)]
    public async Task<IActionResult> Pick(int id, [FromBody] StorefrontPickingRequest request, CancellationToken ct) => Json(await service.RecordPickingAsync(id, request, ct));
    [HttpPost("{id:int}/dispatch"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Dispatch)]
    public async Task<IActionResult> Dispatch(int id, [FromBody] StorefrontDispatchRequest request, CancellationToken ct) => Json(await service.DispatchAsync(id, request, ct));
    [HttpPost("{id:int}/outcome"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.RecordDelivery)]
    public async Task<IActionResult> Outcome(int id, [FromBody] StorefrontDeliveryOutcomeRequest request, CancellationToken ct) => Json(await service.RecordOutcomeAsync(id, request, ct));
    [HttpPost("{id:int}/return"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.ReceiveReturns)]
    public async Task<IActionResult> Return(int id, [FromBody] StorefrontReceiveReturnRequest request, CancellationToken ct) => Json(await service.ReceiveReturnAsync(id, request, ct));
    [HttpPost("{id:int}/incident"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Dispatch)]
    public async Task<IActionResult> Incident(int id, [FromBody] StorefrontIncidentRequest request, CancellationToken ct) => Json(await service.RecordIncidentAsync(id, request, ct));
    [HttpPost("{id:int}/reconcile-incident"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Reconcile)]
    public async Task<IActionResult> ReconcileIncident(int id, [FromBody] StorefrontIncidentReconcileRequest request, CancellationToken ct) => Json(await service.ReconcileIncidentAsync(id, request, ct));
    [HttpPost("{id:int}/customer-paid"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Reconcile)]
    public async Task<IActionResult> CustomerPaid(int id, [FromBody] StorefrontCodCollectionRequest request, CancellationToken ct) => Json(await settlement.RecordCustomerCollectionAsync(id, request, ct));
    [HttpPost("{id:int}/bank-remittance"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Reconcile)]
    public async Task<IActionResult> BankRemittance(int id, [FromBody] StorefrontCodBankReceiptRequest request, CancellationToken ct) => Json(await settlement.ReceiveBankRemittanceAsync(id, request, ct));
    [HttpPost("{id:int}/cash-remittance"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Reconcile)]
    public async Task<IActionResult> CashRemittance(int id, [FromBody] StorefrontCodCashReceiptRequest request, CancellationToken ct) => Json(await settlement.ReceiveCashRemittanceAsync(id, request, ct));
    [HttpPost("{id:int}/withholding"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Reconcile)]
    public async Task<IActionResult> Withholding(int id, [FromBody] StorefrontCarrierWithholdingRequest request, CancellationToken ct) => Json(await settlement.RecordCarrierWithholdingAsync(id, request, ct));
    [HttpPost("{id:int}/post-sale"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.PostSale)]
    public async Task<IActionResult> PostSale(int id, [FromBody] StorefrontOperationRequest request, CancellationToken ct) => Json(await settlement.PostSaleAsync(id, request, ct));
    [HttpPost("import-notification"), ValidateAntiForgeryToken, Authorize(Policy = StorefrontPermissions.Reconcile)]
    public async Task<IActionResult> ImportNotification([FromBody] StorefrontNotificationImport request, CancellationToken ct) => Json(new { globalReceiptId = await bankSources.ImportAuthenticatedNotificationAsync(request.NotificationReceiptId, ct) });
}
public sealed record StorefrontNotificationImport(int NotificationReceiptId);

public sealed class StorefrontCommandExceptionFilterAttribute : Microsoft.AspNetCore.Mvc.Filters.ExceptionFilterAttribute
{
    public override void OnException(Microsoft.AspNetCore.Mvc.Filters.ExceptionContext context)
    {
        if (context.Exception is StorefrontRetryableCommandException retry)
        {
            context.Result = new ObjectResult(new { message = retry.Message, code = "RETRY_SAME_COMMAND", retryable = true }) { StatusCode = 409 };
            context.ExceptionHandled = true;
        }
        else if (context.Exception is GaoApp.Application.DTOs.Delivery.DeliveryFoundationException delivery)
        {
            context.Result = new ObjectResult(new { message = delivery.Message, code = delivery.Code }) { StatusCode = delivery.StatusCode };
            context.ExceptionHandled = true;
        }
    }
}
