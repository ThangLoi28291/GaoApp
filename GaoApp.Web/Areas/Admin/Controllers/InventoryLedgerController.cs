using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-ledger")]
[Authorize]
[Authorize(Policy = PermissionCodes.Inventory.Transaction.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InventoryLedgerController : Controller
{
    private readonly IInventoryLedgerIndexReadService _readService;
    private readonly IAuthorizationService _authorization;

    public InventoryLedgerController(IInventoryLedgerIndexReadService readService, IAuthorizationService authorization)
    {
        _readService = readService;
        _authorization = authorization;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Thẻ kho / Lịch sử giao dịch kho";
        return View();
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetData(
        [FromQuery] InventoryLedgerIndexQueryRequest request,
        CancellationToken ct)
    {
        var result = await _readService.GetPageAsync(request, ct);
        await AddSourceUrlsAsync(result.Items);
        return Ok(result);
    }

    [HttpGet("quick-view")]
    public async Task<IActionResult> GetQuickView(
        [FromQuery] int transactionId,
        CancellationToken ct)
    {
        if (transactionId <= 0)
            return BadRequest(new { message = "Giao dịch cần xem không hợp lệ." });

        var result = await _readService.GetQuickViewAsync(transactionId, ct);
        if (result is not null) await AddSourceUrlsAsync([result.Item]);

        return result is null
            ? NotFound(new { message = "Không tìm thấy giao dịch kho." })
            : Ok(result);
    }

    [HttpGet("period")]
    public async Task<IActionResult> GetPeriod(int warehouseId, int productVariantId,
        DateTime? fromDate, DateTime? toDate, CancellationToken ct)
    {
        if (!ModelState.IsValid || warehouseId <= 0 || productVariantId <= 0
            || fromDate is { Year: < 1900 or > 9998 } || toDate is { Year: < 1900 or > 9998 })
            return BadRequest(new { message = "Kho, sản phẩm hoặc khoảng ngày không hợp lệ." });
        var result = await _readService.GetPeriodAsync(warehouseId, productVariantId, fromDate, toDate, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("source")]
    public async Task<IActionResult> Source(int transactionId, CancellationToken ct)
    {
        var result = await _readService.GetQuickViewAsync(transactionId, ct);
        if (result?.Item.SourceId is null) return NotFound();
        var target = SourceTarget(result.Item);
        if (target is null) return NotFound();
        if (!await CanOpenAsync(result.Item.SourceKind!)) return Forbid();
        return LocalRedirect(Url.Content("~" + target));
    }

    private async Task AddSourceUrlsAsync(IEnumerable<InventoryLedgerIndexItemDto> items)
    {
        var access = new Dictionary<string, bool>();
        foreach (var item in items)
        {
            if (item.SourceKind is null || SourceTarget(item) is null) continue;
            if (!access.TryGetValue(item.SourceKind, out var allowed))
                access[item.SourceKind] = allowed = await CanOpenAsync(item.SourceKind);
            if (allowed) item.SourceUrl = Url.Action(nameof(Source), new { transactionId = item.TransactionId });
        }
    }

    private async Task<bool> CanOpenAsync(string kind)
    {
        string[] policies = kind switch
        {
            "order" => [PermissionCodes.Pos.Order.View],
            "receipt" => [PermissionCodes.Inventory.StockDocument.View, PermissionCodes.Inventory.StockDocument.Update,
                PermissionCodes.Inventory.StockDocument.Approve],
            "purchase-receipt" => [PermissionCodes.Purchase.Receipt.View, PermissionCodes.Purchase.Receipt.Update,
                PermissionCodes.Purchase.Receipt.Approve],
            "count" => [PermissionCodes.Inventory.StockCount.View],
            "transfer" => [PermissionCodes.Inventory.StockTransfer.View],
            "adjustment" => [PermissionCodes.Inventory.Adjustment.View],
            "delivery" => [PermissionCodes.Delivery.View],
            _ => []
        };
        foreach (var policy in policies)
            if ((await _authorization.AuthorizeAsync(User, null, policy)).Succeeded) return true;
        return false;
    }

    private static string? SourceTarget(InventoryLedgerIndexItemDto item) => item.SourceId is not > 0 ? null : item.SourceKind switch
    {
        "order" => $"/admin/pos/order-detail/{item.SourceId}",
        "receipt" or "purchase-receipt" => $"/admin/stock-documents/{item.SourceId}",
        "count" => $"/admin/stock-counts/{item.SourceId}",
        "transfer" => $"/admin/stock-transfers/{item.SourceId}",
        "adjustment" => $"/admin/inventory-adjustment-documents/{item.SourceId}",
        "delivery" => $"/admin/deliveries/{item.SourceId}/bill",
        _ => null
    };
}
