using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize, ApiController, AutoValidateAntiforgeryToken]
[Route("admin/api/stock-documents/{documentId:int}/barcode-proposals")]
public sealed class ReceiptBarcodeProposalsController(
    IReceiptBarcodeProposalService service, ITenantContext tenant, IAuthorizationService authorization) : ControllerBase
{
    private int StoreId => tenant.StoreId ?? 0;
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpGet]
    public Task<IActionResult> Get(int documentId, CancellationToken ct) => Call(async () =>
    {
        var context = await service.ContextAsync(StoreId, documentId, ct);
        var canPropose = await Has(context, "update");
        var canReview = await Has(context, "approve");
        if (!canPropose && !canReview && !await Has(context, "view")) return Forbid();
        return Ok(new
        {
            items = await service.ListAsync(StoreId, documentId, ct),
            canPropose = canPropose && context.Status is StockDocumentStatus.Draft or StockDocumentStatus.Rejected,
            canReview = canReview && context.Status is StockDocumentStatus.PendingApproval or StockDocumentStatus.Confirmed
        });
    });

    [HttpGet("lookup")]
    public Task<IActionResult> Lookup(int documentId, string? term, bool catalogOnly, CancellationToken ct) => Call(async () =>
    {
        var context = await service.ContextAsync(StoreId, documentId, ct);
        if (!await Has(context, "update") && !await Has(context, "approve")) return Forbid();
        var items = await service.SearchAsync(StoreId, documentId, term ?? "", catalogOnly, ct);
        return Ok(new { results = items.Select(x => new
        {
            id = x.ProductUnitConversionId?.ToString() ?? $"{x.ProductVariantId}_{x.UnitId}",
            x.ProductVariantId, x.ProductUnitConversionId, x.UnitId, x.ProductName, x.Sku, x.Barcode,
            x.UnitName, x.BaseUnitName, x.IsBaseUnit, x.Factor, x.ImageUrl, x.Price, x.Text, x.SourceType
        }) });
    });

    [HttpGet("products/{variantId:int}/units")]
    public Task<IActionResult> Units(int documentId, int variantId, CancellationToken ct) => Call(async () =>
    {
        var context = await service.ContextAsync(StoreId, documentId, ct);
        if (!await Has(context, "update") && !await Has(context, "approve")) return Forbid();
        return Ok(new { items = await service.UnitsAsync(StoreId, documentId, variantId, ct) });
    });

    [HttpPost]
    public Task<IActionResult> Propose(int documentId, ProposeReceiptBarcodeRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!await Has(await service.ContextAsync(StoreId, documentId, ct), "update")) return Forbid();
        var item = await service.ProposeAsync(StoreId, documentId, UserId, request, ct);
        return Ok(new { item });
    });

    public sealed class DecisionRequest { public bool Approve { get; set; } public string? Note { get; set; } }

    [HttpPost("{requestId:int}/review")]
    public Task<IActionResult> Review(int documentId, int requestId, DecisionRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!await Has(await service.ContextAsync(StoreId, documentId, ct), "approve")) return Forbid();
        await service.ResolveAsync(StoreId, documentId, requestId, UserId,
            request.Approve ? BarcodeVerificationRequestStatus.Approved : BarcodeVerificationRequestStatus.Rejected, request.Note, ct);
        return Ok(new { message = request.Approve ? "Đã thêm mã hãng, giữ nguyên mã nội bộ." : "Đã từ chối mã. Hàng nhập và quy đổi trên phiếu được giữ nguyên." });
    });

    private async Task<bool> Has(ReceiptBarcodeContext context, string action)
    {
        var po = context.ReceiptSource == PurchaseReceiptSource.PurchaseOrder;
        var permission = action switch
        {
            "update" => po ? PermissionCodes.Purchase.Receipt.Update : PermissionCodes.Inventory.StockDocument.Update,
            "approve" => po ? PermissionCodes.Purchase.Receipt.Approve : PermissionCodes.Inventory.StockDocument.Approve,
            _ => po ? PermissionCodes.Purchase.Receipt.View : PermissionCodes.Inventory.StockDocument.View
        };
        return (await authorization.AuthorizeAsync(User, permission)).Succeeded;
    }

    private async Task<IActionResult> Call(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (BusinessRuleException ex) { return Conflict(new { message = ex.SafeMessage }); }
    }
}
