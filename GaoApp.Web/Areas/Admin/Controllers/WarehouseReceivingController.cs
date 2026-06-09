using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/warehouse-receiving")]
[Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
public class WarehouseReceivingController : Controller
{
    private readonly IStockDocumentService _stockDocumentService;
    private readonly IBarcodeLookupService _barcodeLookupService;

    public WarehouseReceivingController(
        IStockDocumentService stockDocumentService,
        IBarcodeLookupService barcodeLookupService)
    {
        _stockDocumentService = stockDocumentService;
        _barcodeLookupService = barcodeLookupService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var model = await _stockDocumentService.GetDetailAsync(id, ct);
        if (model == null)
            return NotFound();

        // Nhân viên không xem phiếu đã duyệt / đã hủy ở màn này.
        if (model.Status == StockDocumentStatus.Confirmed ||
            model.Status == StockDocumentStatus.Cancelled)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceipts(CancellationToken ct)
    {
        var items = await _stockDocumentService.GetReceiptListAsync(ct);

        var result = items
            .Where(x =>
                x.Status == StockDocumentStatus.Draft ||
                x.Status == StockDocumentStatus.PendingApproval ||
                x.Status == StockDocumentStatus.Rejected)
            .OrderBy(x => x.Status == StockDocumentStatus.Rejected ? 0 :
                          x.Status == StockDocumentStatus.Draft ? 1 :
                          x.Status == StockDocumentStatus.PendingApproval ? 2 : 9)
            .ThenByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc ?? x.DocumentDate)
            .Select(x => new
            {
                x.Id,
                x.DocumentNo,
                x.DocumentTitle,
                x.DocumentDate,
                x.WarehouseName,
                x.SupplierName,
                x.Status,
                x.TotalLines,
                x.TotalProductTypes,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.SubmittedAtUtc
            })
            .ToList();

        return Ok(result);
    }

    [HttpPost("receipts")]
    public async Task<IActionResult> CreateReceipt(
        [FromBody] CreateStockDocumentRequest request,
        CancellationToken ct)
    {
        var id = await _stockDocumentService.CreateReceiptAsync(request, ct);

        return Ok(new
        {
            message = "Đã tạo phiếu nhập.",
            id,
            redirectUrl = Url.Action(nameof(Detail), "WarehouseReceiving", new { area = "Admin", id })
        });
    }

    /// <summary>
    /// Lookup riêng cho màn nhân viên.
    /// Không trả giá vốn/giá nhập ra UI.
    /// </summary>
    [HttpGet("product-lookup-select2")]
    public async Task<IActionResult> ProductLookupSelect2([FromQuery] string term, CancellationToken ct)
    {
        var items = await _barcodeLookupService.SearchForStockDocumentSelect2Async(term, 20, ct);

        return Json(new
        {
            results = items.Select(x => new
            {
                id = $"{x.ProductVariantId}_{x.UnitId ?? 0}",
                text = x.Text,
                productVariantId = x.ProductVariantId,
                unitId = x.UnitId,
                productName = x.ProductName,
                sku = x.Sku,
                barcode = x.Barcode,
                unitName = x.UnitName,
                factor = x.Factor,
                imageUrl = x.ImageUrl,
                price = x.Price,
                sourceType = x.SourceType
            })
        });
    }
    [HttpGet("{id:int}/lines")]
    public async Task<IActionResult> Lines(int id, CancellationToken ct)
    {
        var model = await _stockDocumentService.GetDetailAsync(id, ct);
        if (model == null)
            return NotFound();

        return PartialView("_ReceivingLinesTable", model);
    }
}