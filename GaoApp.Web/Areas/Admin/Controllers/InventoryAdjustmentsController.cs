using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/inventory-adjustments")]
[ApiController]
public class InventoryAdjustmentsController : ControllerBase
{
    private readonly IInventoryAdjustmentService _inventoryAdjustmentService;
    private readonly IBarcodeLookupService _barcodeLookupService;

    public InventoryAdjustmentsController(
        IInventoryAdjustmentService inventoryAdjustmentService,
        IBarcodeLookupService barcodeLookupService)
    {
        _inventoryAdjustmentService = inventoryAdjustmentService;
        _barcodeLookupService = barcodeLookupService;
    }

    /// <summary>
    /// Tạo điều chỉnh kho thủ công.
    /// Quantity gửi lên là số lượng theo đơn vị user đang chọn.
    /// Backend sẽ tự quy đổi về số lượng gốc.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStockAdjustmentRequest request, CancellationToken ct)
    {
        var result = await _inventoryAdjustmentService.CreateAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lookup sản phẩm / đơn vị cho Select2 của màn hình Adjustment.
    /// Dùng lại logic search cũ từ StockDocument nhưng map sang DTO đúng ngữ cảnh.
    /// </summary>
    [HttpGet("product-lookup-select2")]
    public async Task<IActionResult> ProductLookupSelect2([FromQuery] string? term, CancellationToken ct)
    {
        var items = await _barcodeLookupService.SearchForInventoryAdjustmentSelect2Async(term ?? string.Empty, 20, ct);

        var results = items.Select(x => new
        {
            id = $"{x.ProductVariantId}_{x.UnitId}",
            productVariantId = x.ProductVariantId,
            unitId = x.UnitId,
            productName = x.ProductName,
            sku = x.Sku,
            barcode = x.Barcode,
            unitName = x.UnitName,
            factor = x.Factor,
            isBaseUnitFallback = x.IsBaseUnitFallback,
            sourceType = x.SourceType,
            text = x.Text
        });

        return Ok(new { results });
    }

    /// <summary>
    /// Lookup barcode cho màn hình Adjustment.
    /// Có thể quét barcode đơn vị hoặc barcode variant.
    /// </summary>
    [HttpGet("barcode-lookup")]
    public async Task<IActionResult> BarcodeLookup([FromQuery] string barcode, CancellationToken ct)
    {
        var result = await _barcodeLookupService.FindAsync(barcode, ct);
        if (result == null)
            return NotFound(new { message = "Không tìm thấy sản phẩm theo barcode." });

        return Ok(result);
    }
}