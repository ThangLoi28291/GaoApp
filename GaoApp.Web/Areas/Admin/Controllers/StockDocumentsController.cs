using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Areas.Admin.Controllers;

/// <summary>
/// API backend cho module phiếu nhập kho.
/// 
/// Nguyên tắc phân quyền:
/// - View    : xem danh sách / xem chi tiết / dữ liệu hỗ trợ chỉ-đọc
/// - Create  : tạo phiếu / sửa phiếu / thêm-sửa-xóa dòng / gửi duyệt
/// - Approve : duyệt / từ chối phiếu
/// 
/// Lưu ý:
/// - Không chỉ ẩn UI, backend bắt buộc phải có [Authorize].
/// - Đây là lớp quan trọng để tránh gọi thẳng AJAX vượt quyền.
/// </summary>
[Area("Admin")]
[Route("admin/api/stock-documents")]
[ApiController]
[Authorize] // yêu cầu đã đăng nhập trước, sau đó từng action sẽ chặn theo policy cụ thể
public class StockDocumentsController : ControllerBase
{
    private readonly IStockDocumentService _stockDocumentService;
    private readonly IBarcodeLookupService _barcodeLookupService;
    private readonly IPOSService _posService;
    private readonly AppDbContext _context;

    public StockDocumentsController(
        IStockDocumentService stockDocumentService,
        IBarcodeLookupService barcodeLookupService,
        IPOSService posService,
        AppDbContext context)
    {
        _stockDocumentService = stockDocumentService;
        _barcodeLookupService = barcodeLookupService;
        _posService = posService;
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách đơn vị của 1 biến thể sản phẩm.
    /// API này có thể dùng ở màn xem chi tiết nên chỉ cần quyền xem.
    /// </summary>
    [HttpGet("product-variants/{variantId:int}/units")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentView)]
    public async Task<IActionResult> GetVariantUnits(int variantId, CancellationToken ct)
    {
        var variant = await _context.ProductVariants
            .Include(x => x.Product)
                .ThenInclude(x => x.BaseUnit)
            .FirstOrDefaultAsync(x => x.Id == variantId, ct);

        if (variant == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy biến thể sản phẩm."
            });
        }

        var conversions = await _context.ProductUnitConversions
            .Include(x => x.Unit)
            .Where(x => x.ProductVariantId == variantId && x.IsActive)
            .OrderByDescending(x => x.IsBaseUnit)
            .ThenByDescending(x => x.IsDefaultForSale)
            .ThenBy(x => x.SortOrder)
            .Select(x => new
            {
                unitId = x.UnitId,
                unitName = x.Unit.Name,
                factor = x.Factor,
                isBaseUnit = x.IsBaseUnit,
                isDefaultForSale = x.IsDefaultForSale
            })
            .ToListAsync(ct);

        if (conversions.Any())
        {
            return Ok(conversions);
        }

        // Fallback: chưa có ProductUnitConversion thì dùng BaseUnit của Product
        if (variant.Product?.BaseUnit == null)
        {
            return Ok(new List<object>());
        }

        return Ok(new[]
        {
            new
            {
                unitId = variant.Product.BaseUnitId,
                unitName = variant.Product.BaseUnit.Name,
                factor = 1m,
                isBaseUnit = true,
                isDefaultForSale = true
            }
        });
    }

    /// <summary>
    /// Lấy danh sách phiếu nhập.
    /// Chỉ cần quyền xem chứng từ kho.
    /// </summary>
    [HttpGet("receipts")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentView)]
    public async Task<IActionResult> GetReceiptList(CancellationToken ct)
    {
        var result = await _stockDocumentService.GetReceiptListAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Tạo phiếu nhập kho mới.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpPost("receipts")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
    public async Task<IActionResult> CreateReceipt(
        [FromBody] CreateStockDocumentRequest request,
        CancellationToken ct)
    {
        var id = await _stockDocumentService.CreateReceiptAsync(request, ct);

        return Ok(new
        {
            message = "Tạo phiếu nhập kho thành công.",
            id
        });
    }

    /// <summary>
    /// Lấy chi tiết phiếu nhập.
    /// Chỉ cần quyền xem.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentView)]
    public async Task<IActionResult> GetDetail(int id, CancellationToken ct)
    {
        var result = await _stockDocumentService.GetDetailAsync(id, ct);
        if (result == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy phiếu nhập kho."
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Tra cứu barcode để thêm dòng vào chứng từ.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpGet("lookup-barcode")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
    public async Task<IActionResult> LookupBarcode([FromQuery] string barcode, CancellationToken ct)
    {
        var result = await _barcodeLookupService.FindAsync(barcode, ct);

        if (result == null)
        {
            return Ok(new
            {
                found = false,
                message = "Không tìm thấy sản phẩm theo barcode."
            });
        }

        return Ok(new
        {
            found = true,
            productId = result.ProductId,
            productName = result.ProductName,
            productVariantId = result.ProductVariantId,
            variantSku = result.VariantSku,
            unitId = result.UnitId,
            unitName = result.UnitName,
            baseUnitId = result.BaseUnitId,
            baseUnitName = result.BaseUnitName,
            factor = result.Factor,
            isBaseUnit = result.IsBaseUnit,
            isDefaultForSale = result.IsDefaultForSale,
            barcode = result.Barcode,
            sourceType = result.SourceType
        });
    }

    /// <summary>
    /// Tìm sản phẩm phục vụ thêm dòng chứng từ.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpGet("search-products")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
    public async Task<IActionResult> SearchProducts([FromQuery] string keyword, CancellationToken ct)
    {
        var result = await _posService.SearchProductsForPOSAsync(keyword, 20, ct);
        return Ok(result);
    }

    /// <summary>
    /// Thêm dòng chứng từ.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpPost("{id:int}/lines")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
    public async Task<IActionResult> AddLine(
        int id,
        [FromBody] AddStockDocumentLineRequest request,
        CancellationToken ct)
    {
        var lineId = await _stockDocumentService.AddLineAsync(id, request, ct);

        return Ok(new
        {
            message = "Thêm dòng phiếu nhập kho thành công.",
            lineId
        });
    }

    /// <summary>
    /// Thêm dòng bằng barcode.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpPost("{id:int}/lines/by-barcode")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
    public async Task<IActionResult> AddLineByBarcode(
        int id,
        [FromBody] AddStockDocumentLineByBarcodeRequest request,
        CancellationToken ct)
    {
        try
        {
            var lineId = await _stockDocumentService.AddLineByBarcodeAsync(id, request, ct);

            return Ok(new
            {
                message = "Thêm dòng bằng barcode thành công.",
                lineId
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (DbUpdateException ex)
        {
            return StatusCode(500, new
            {
                message = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Cập nhật dòng chứng từ.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpPut("{documentId:int}/lines/{lineId:int}")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
    public async Task<IActionResult> UpdateLine(
        int documentId,
        int lineId,
        [FromBody] UpdateStockDocumentLineRequest request,
        CancellationToken ct)
    {
        await _stockDocumentService.UpdateLineAsync(lineId, request, ct);

        return Ok(new
        {
            message = "Cập nhật dòng phiếu nhập kho thành công."
        });
    }

    /// <summary>
    /// Xóa dòng chứng từ.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpDelete("{documentId:int}/lines/{lineId:int}")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
    public async Task<IActionResult> DeleteLine(
        int documentId,
        int lineId,
        CancellationToken ct)
    {
        await _stockDocumentService.DeleteLineAsync(lineId, ct);

        return Ok(new
        {
            message = "Xóa dòng phiếu nhập kho thành công."
        });
    }

    /// <summary>
    /// Gửi phiếu sang chờ duyệt.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpPost("{id:int}/submit-approval")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentCreate)]
    public async Task<IActionResult> SubmitApproval(
        int id,
        [FromBody] ApprovalActionRequest? request,
        CancellationToken ct)
    {
        await _stockDocumentService.SubmitForApprovalAsync(id, request?.Note, ct);

        return Ok(new
        {
            message = "Đã gửi phiếu sang chờ duyệt.",
            redirectUrl = Url.Action("Index", "StockDocumentManagement", new { area = "Admin" })
        });
    }

    /// <summary>
    /// Duyệt phiếu nhập kho.
    /// Cần quyền duyệt chứng từ kho.
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentApprove)]
    public async Task<IActionResult> Approve(
        int id,
        [FromBody] ApprovalActionRequest? request,
        CancellationToken ct)
    {
        await _stockDocumentService.ApproveAsync(id, request?.Note, ct);

        return Ok(new
        {
            message = "Đã duyệt phiếu nhập kho thành công.",
            redirectUrl = Url.Action("Index", "StockDocumentManagement", new { area = "Admin" })
        });
    }

    /// <summary>
    /// Từ chối duyệt phiếu nhập kho.
    /// Cần quyền duyệt chứng từ kho.
    /// </summary>
    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = AppPermissions.InventoryStockDocumentApprove)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] ApprovalActionRequest? request,
        CancellationToken ct)
    {
        await _stockDocumentService.RejectAsync(id, request?.Note, ct);

        return Ok(new
        {
            message = "Đã từ chối duyệt phiếu.",
            redirectUrl = Url.Action("Index", "StockDocumentManagement", new { area = "Admin" })
        });
    }
}