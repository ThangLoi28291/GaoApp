using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize, ApiController, AutoValidateAntiforgeryToken]
[Route("admin/api/stock-documents/{documentId:int}/intake")]
public sealed class ReceiptIntakeController(IReceiptIntakeService intake,
    IStockDocumentProvisionalItemService provisional, IReceiptBarcodeProposalService barcodes,
    IProcurementCatalogService catalog, ITenantContext tenant, IAuthorizationService authorization, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(int documentId, CancellationToken ct) => Call(async () =>
    {
        var context = await Context(documentId, ct);
        var update = await Has(context, "update"); var approve = await Has(context, "approve");
        if (!update && !approve && !await Has(context, "view")) return Forbid();
        var state = await provisional.GetAsync(documentId, ct);
        var permissions = await CatalogPermissions();
        return Ok(new
        {
            brands = await db.Brands.Where(x => x.StoreId == tenant.StoreId && x.IsActive && !x.IsDeleted).OrderBy(x => x.Name).Select(x => new { id = x.Id, text = x.Name }).ToListAsync(ct),
            state, permissions, recentReceipts = await intake.GetRecentAsync(documentId, ct),
            canCapture = update && state.PermittedActions.Contains("Capture"),
            canReview = approve && (context.Status == StockDocumentStatus.PendingApproval ||
                (update && state.PermittedActions.Contains("Capture"))),
            options = update || approve ? await catalog.GetQuickCreateOptionsAsync(ct) : new ProcurementQuickCreateOptionsDto()
        });
    });

    [HttpGet("review-products")]
    public Task<IActionResult> ReviewProducts(int documentId, string? term, CancellationToken ct) => Call(async () =>
    {
        if (!await Has(await Context(documentId, ct), "approve")) return Forbid();
        term = term?.Trim();
        if (string.IsNullOrWhiteSpace(term)) return Ok(new { results = Array.Empty<object>() });
        if (term.Length > 200) term = term[..200];
        var normalizedTerm = GaoApp.Application.Common.Helpers.ProductVariantNameHelper.NormalizeForSearch(term);
        var units = await db.ProductUnitConversions.AsNoTracking().Where(x => x.StoreId == tenant.StoreId && !x.IsDeleted &&
            x.ProductVariant.StoreId == tenant.StoreId && x.ProductVariant.Product.StoreId == tenant.StoreId && x.Unit.StoreId == tenant.StoreId &&
            x.ProductVariant.Product.BaseUnit.StoreId == tenant.StoreId && !x.ProductVariant.IsDeleted && !x.ProductVariant.Product.IsDeleted && !x.Unit.IsDeleted &&
            (EF.Functions.Collate(x.ProductVariant.Product.Name.Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(normalizedTerm) ||
             EF.Functions.Collate(x.ProductVariant.ProductVariantName!.Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(normalizedTerm) ||
             x.ProductVariant.Sku.Contains(term) || x.Barcodes.Any(b => b.Barcode == term && !b.IsDeleted)))
            .OrderBy(x => x.ProductVariant.Product.Name).ThenBy(x => x.Factor).Take(30)
            .Select(x => new { id = x.Id, productVariantId = x.ProductVariantId,
                name = x.ProductVariant.ProductVariantName ?? x.ProductVariant.Product.Name,
                sku = x.ProductVariant.Sku, unitId = x.UnitId, unitName = x.Unit.Name, factor = x.Factor,
                baseUnitId = x.ProductVariant.Product.BaseUnitId, baseUnitName = x.ProductVariant.Product.BaseUnit.Name,
                active = x.IsActive && x.Unit.IsActive && x.ProductVariant.IsActive && x.ProductVariant.Product.IsActive,
                sellable = x.ProductVariant.Product.IsSellable }).ToListAsync(ct);
        return Ok(new { results = units.Select(x => new {
            x.id, x.productVariantId, x.name, x.unitId, x.unitName, x.factor, x.baseUnitId, x.baseUnitName,
            disabled = !x.active, text = $"{x.name} · {x.sku} · {x.unitName} ×{x.factor}" +
                (!x.active ? " · Ngừng hoạt động – cần kích hoạt trong danh mục" : !x.sellable ? " · Chưa bán POS" : "")
        }) });
    });

    [HttpPost]
    [RequestSizeLimit(400000)]
    public Task<IActionResult> Capture(int documentId, CaptureReceiptIntakeRequest request, CancellationToken ct) => Call(async () =>
    {
        var context = await Context(documentId, ct);
        if (!await Has(context, "update") || (request.ApproveNow && !await Has(context, "approve"))) return Forbid();
        var state = await intake.CaptureIntakeAsync(documentId, request, await CatalogPermissions(), ct);
        state.RecentReceipts = await intake.GetRecentAsync(documentId, ct);
        return Ok(state);
    });

    [HttpPost("known")]
    [RequestSizeLimit(400000)]
    public Task<IActionResult> Known(int documentId, RecordKnownReceiptItemRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!await Has(await Context(documentId, ct), "update")) return Forbid();
        var state = await intake.RecordKnownAsync(documentId, request, ct);
        state.RecentReceipts = await intake.GetRecentAsync(documentId, ct);
        return Ok(state);
    });

    [HttpPost("{itemId:int}/review")]
    [RequestSizeLimit(400000)]
    public Task<IActionResult> Review(int documentId, int itemId, ReviewReceiptIntakeRequest request, CancellationToken ct) => Call(async () =>
    {
        var context = await Context(documentId, ct);
        if (!await Has(context, "approve") || (context.Status != StockDocumentStatus.PendingApproval && !await Has(context, "update"))) return Forbid();
        return Ok(await intake.ReviewIntakeAsync(documentId, itemId, request, await CatalogPermissions(), ct));
    });

    [HttpPost("{itemId:int}/quantity")]
    public Task<IActionResult> Quantity(int documentId, int itemId, UpdateReceiptIntakeQuantityRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!await Has(await Context(documentId, ct), "update")) return Forbid();
        return Ok(await intake.UpdateQuantityAsync(documentId, itemId, request, ct));
    });

    [HttpPost("{itemId:int}/remove")]
    public Task<IActionResult> Remove(int documentId, int itemId, RemoveProvisionalItemRequest request, CancellationToken ct) => Call(async () =>
    {
        if (!await Has(await Context(documentId, ct), "update")) return Forbid();
        return Ok(await provisional.RemoveAsync(documentId, itemId, request, false, ct));
    });

    private Task<ReceiptBarcodeContext> Context(int documentId, CancellationToken ct)
        => barcodes.ContextAsync(tenant.StoreId ?? 0, documentId, ct);

    [HttpGet("{itemId:int}/photo")]
    public Task<IActionResult> Photo(int documentId, int itemId, CancellationToken ct) => Call(async () =>
    {
        var context = await Context(documentId, ct);
        if (!await Has(context, "update") && !await Has(context, "approve") && !await Has(context, "view")) return Forbid();
        var photo = await intake.GetPhotoAsync(documentId, itemId, ct);
        if (photo is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(photo, "image/jpeg");
    });

    [HttpGet("{itemId:int}/review-photo")]
    public Task<IActionResult> ReviewPhoto(int documentId, int itemId, CancellationToken ct) => Call(async () =>
    {
        var context = await Context(documentId, ct);
        if (!await Has(context, "approve") && !await Has(context, "view")) return Forbid();
        var photo = await db.StockDocumentProvisionalItems.AsNoTracking()
            .Where(x => x.StoreId == tenant.StoreId && x.StockDocumentId == documentId && x.Id == itemId && !x.IsDeleted)
            .Select(x => x.ReviewPhoto).SingleOrDefaultAsync(ct);
        if (photo is null) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(photo, "image/jpeg");
    });

    private Task<bool> Has(ReceiptBarcodeContext context, string action)
    {
        var po = context.ReceiptSource == PurchaseReceiptSource.PurchaseOrder;
        return Has(action switch
        {
            "update" => po ? PermissionCodes.Purchase.Receipt.Update : PermissionCodes.Inventory.StockDocument.Update,
            "approve" => po ? PermissionCodes.Purchase.Receipt.Approve : PermissionCodes.Inventory.StockDocument.Approve,
            _ => po ? PermissionCodes.Purchase.Receipt.View : PermissionCodes.Inventory.StockDocument.View
        });
    }
    private async Task<bool> Has(string policy) => (await authorization.AuthorizeAsync(User, policy)).Succeeded;
    private async Task<ReceiptIntakePermissions> CatalogPermissions() => new(
        await Has(PermissionCodes.Catalog.Product.Create), await Has(PermissionCodes.Catalog.Product.Update),
        await Has(PermissionCodes.Catalog.Unit.Create), await Has(PermissionCodes.Catalog.Barcode.Create));
    private async Task<IActionResult> Call(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (BusinessRuleException ex) { return Conflict(new { message = ex.SafeMessage }); }
    }
}
