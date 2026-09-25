using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/stock-documents/{stockDocumentId:int}/provisional-items")]
[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class StockDocumentProvisionalItemsController : Controller
{
    private readonly IStockDocumentProvisionalItemService _service;
    private readonly IProcurementCatalogService _catalog;
    private readonly IBarcodeLookupService _barcodes;
    private readonly IAuthorizationService _authorization;
    private readonly IConfiguration _configuration;

    public StockDocumentProvisionalItemsController(
        IStockDocumentProvisionalItemService service,
        IProcurementCatalogService catalog,
        IBarcodeLookupService barcodes,
        IAuthorizationService authorization,
        IConfiguration configuration)
    {
        _service = service;
        _catalog = catalog;
        _barcodes = barcodes;
        _authorization = authorization;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> Get(int stockDocumentId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasViewAsync(stockDocumentId, ct)) return Forbid();
        return await JsonCall(() => _service.GetAsync(stockDocumentId, ct));
    }

    [HttpGet("options")]
    public async Task<IActionResult> Options(int stockDocumentId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasManagerAsync(stockDocumentId, ct)) return Forbid();
        return Json(await _catalog.GetQuickCreateOptionsAsync(ct));
    }

    [HttpGet("capture-options")]
    public async Task<IActionResult> CaptureOptions(int stockDocumentId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasWarehouseAsync(stockDocumentId, ct)) return Forbid();
        var options = await _catalog.GetQuickCreateOptionsAsync(ct);
        return Json(new { options.Units });
    }

    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates(
        int stockDocumentId, [FromQuery] string? term, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasManagerAsync(stockDocumentId, ct)) return Forbid();
        var items = await _barcodes.SearchForStockDocumentSelect2Async(term ?? string.Empty, 20, ct);
        var duplicateCounts = items.Where(x => x.ProductUnitConversionId.HasValue)
            .GroupBy(x => new { x.ProductVariantId, x.ProductUnitConversionId })
            .ToDictionary(x => x.Key, x => x.Count());
        return Json(new
        {
            results = items.Where(x => x.ProductUnitConversionId.HasValue).Select(x => new
            {
                id = x.ProductUnitConversionId,
                x.ProductVariantId,
                productUnitConversionId = x.ProductUnitConversionId,
                text = x.Text,
                x.ProductName,
                x.Sku,
                x.UnitName,
                x.Factor,
                x.Barcode,
                duplicateCandidateCount = duplicateCounts[new
                    { x.ProductVariantId, x.ProductUnitConversionId }]
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Capture(
        int stockDocumentId, [FromBody] CaptureProvisionalItemRequest request,
        CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasWarehouseAsync(stockDocumentId, ct)) return Forbid();
        return await JsonCall(() => _service.CaptureAsync(stockDocumentId, request, ct));
    }

    [HttpPost("{itemId:int}/increment")]
    public async Task<IActionResult> Increment(
        int stockDocumentId, int itemId,
        [FromBody] IncrementProvisionalItemRequest request, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasWarehouseAsync(stockDocumentId, ct)) return Forbid();
        return await JsonCall(() => _service.IncrementAsync(stockDocumentId, itemId, request, ct));
    }

    [HttpPost("{itemId:int}/edit")]
    public async Task<IActionResult> Edit(
        int stockDocumentId, int itemId,
        [FromBody] EditProvisionalItemRequest request, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasWarehouseAsync(stockDocumentId, ct)) return Forbid();
        return await JsonCall(() => _service.EditAsync(stockDocumentId, itemId, request, ct));
    }

    [HttpPost("{itemId:int}/remove")]
    public async Task<IActionResult> Remove(
        int stockDocumentId, int itemId,
        [FromBody] RemoveProvisionalItemRequest request, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var manager = await HasManagerAsync(stockDocumentId, ct);
        if (!manager && !await HasWarehouseAsync(stockDocumentId, ct)) return Forbid();
        return await JsonCall(() => _service.RemoveAsync(
            stockDocumentId, itemId, request, manager, ct));
    }

    [HttpPost("undo")]
    public async Task<IActionResult> Undo(
        int stockDocumentId, [FromBody] ProvisionalReceivingMutationRequest request,
        CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasWarehouseAsync(stockDocumentId, ct)) return Forbid();
        return await JsonCall(() => _service.UndoAsync(stockDocumentId, request, ct));
    }

    [HttpPost("{itemId:int}/link")]
    public async Task<IActionResult> Link(
        int stockDocumentId, int itemId,
        [FromBody] ResolveProvisionalItemRequest request, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasManagerAsync(stockDocumentId, ct)) return Forbid();
        var canBarcode = await HasAsync(PermissionCodes.Catalog.Barcode.Create);
        return await JsonCall(() => _service.LinkExistingAsync(
            stockDocumentId, itemId, request, canBarcode, ct));
    }

    [HttpPost("{itemId:int}/quick-create")]
    public async Task<IActionResult> QuickCreate(
        int stockDocumentId, int itemId,
        [FromBody] QuickCreateAndResolveProvisionalItemRequest request,
        CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasManagerAsync(stockDocumentId, ct) ||
            !await HasAsync(PermissionCodes.Catalog.Product.Create)) return Forbid();
        var canUnit = await HasAsync(PermissionCodes.Catalog.Unit.Create);
        var canBarcode = await HasAsync(PermissionCodes.Catalog.Barcode.Create);
        return await JsonCall(() => _service.QuickCreateAndResolveAsync(
            stockDocumentId, itemId, request, canUnit, canBarcode, ct));
    }

    private async Task<IActionResult> JsonCall(Func<Task<ProvisionalReceivingStateDto>> action)
    {
        try { return Json(await action()); }
        catch (BusinessRuleException ex) { return Conflict(new { message = ex.SafeMessage }); }
    }

    private bool Enabled => _configuration.GetValue<bool>(
        "ReceivingWorkbench:ProvisionalItemsEnabled");

    private async Task<bool> HasViewAsync(int stockDocumentId, CancellationToken ct)
    {
        var source = (await _service.GetAsync(stockDocumentId, ct)).ReceiptSource;
        return await HasAsync(source == PurchaseReceiptSource.PurchaseOrder
            ? PermissionCodes.Purchase.Receipt.View
            : PermissionCodes.Inventory.StockDocument.View);
    }

    private async Task<bool> HasWarehouseAsync(int stockDocumentId, CancellationToken ct)
    {
        var source = (await _service.GetAsync(stockDocumentId, ct)).ReceiptSource;
        return await HasAsync(source == PurchaseReceiptSource.PurchaseOrder
            ? PermissionCodes.Purchase.Receipt.Update
            : PermissionCodes.Inventory.StockDocument.Update);
    }

    private async Task<bool> HasManagerAsync(int stockDocumentId, CancellationToken ct)
    {
        var source = (await _service.GetAsync(stockDocumentId, ct)).ReceiptSource;
        return await HasAsync(source == PurchaseReceiptSource.PurchaseOrder
            ? PermissionCodes.Purchase.Receipt.Approve
            : PermissionCodes.Inventory.StockDocument.Approve);
    }

    private async Task<bool> HasAnyAsync(params string[] permissions)
    {
        foreach (var permission in permissions)
            if (await HasAsync(permission)) return true;
        return false;
    }

    private async Task<bool> HasAsync(string permission)
        => (await _authorization.AuthorizeAsync(User, permission)).Succeeded;
}
