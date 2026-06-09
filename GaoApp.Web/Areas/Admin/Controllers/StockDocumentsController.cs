using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
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
    private readonly IStockDocumentLookupService _stockDocumentLookupService;
    private readonly ITenantContext _tenantContext;
    private readonly IInputInvoiceXmlService _inputInvoiceXmlService;

    public StockDocumentsController(
      IStockDocumentService stockDocumentService,
      IStockDocumentLookupService stockDocumentLookupService,
      IBarcodeLookupService barcodeLookupService,
      IPOSService posService,
      ITenantContext tenantContext,
IInputInvoiceXmlService inputInvoiceXmlService)
    {
        _stockDocumentService = stockDocumentService;
        _stockDocumentLookupService = stockDocumentLookupService;
        _barcodeLookupService = barcodeLookupService;
        _posService = posService;
        _tenantContext = tenantContext;
        _inputInvoiceXmlService = inputInvoiceXmlService;
    }


    /// <summary>
    /// Lấy danh sách đơn vị của 1 biến thể sản phẩm.
    /// API này có thể dùng ở màn xem chi tiết nên chỉ cần quyền xem.
    /// </summary>

    [HttpGet("product-variants/{variantId:int}/units")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Create)]
    public async Task<IActionResult> GetVariantUnits(int variantId, CancellationToken ct)
    {
        if (variantId <= 0)
        {
            return BadRequest(new
            {
                message = "Variant không hợp lệ."
            });
        }

        var units = await _stockDocumentLookupService.GetVariantUnitsAsync(variantId, ct);

        if (units == null || units.Count == 0)
        {
            return NotFound(new
            {
                message = "Không tìm thấy biến thể sản phẩm hoặc chưa cấu hình đơn vị."
            });
        }

        return Ok(units.Select(x => new
        {
            unitId = x.UnitId,
            unitName = x.UnitName,
            factor = x.Factor,
            isBaseUnit = x.IsBaseUnit,
            isDefaultForSale = x.IsDefaultForSale
        }));
    }

    /// <summary>
    /// Lấy danh sách phiếu nhập.
    /// Chỉ cần quyền xem chứng từ kho.
    /// </summary>
    [HttpGet("receipts")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.View)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Create)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.View)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Create)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Create)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
    public async Task<IActionResult> AddLineByBarcode(
    int id,
    [FromBody] AddStockDocumentLineByBarcodeRequest request,
    CancellationToken ct)
    {
        var lineId = await _stockDocumentService.AddLineByBarcodeAsync(id, request, ct);

        return Ok(new
        {
            message = "Thêm dòng bằng barcode thành công.",
            lineId
        });
    }

    /// <summary>
    /// Cập nhật dòng chứng từ.
    /// Cần quyền tạo/sửa chứng từ kho.
    /// </summary>
    [HttpPut("{documentId:int}/lines/{lineId:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Delete)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Create)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Cancel)]
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
    [HttpPost("{id:int}/input-invoices/upload-xml")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> UploadInputInvoiceXml(
    int id,
    IFormFile file,
    CancellationToken ct)
    {
        if (!_tenantContext.StoreId.HasValue)
        {
            return BadRequest(new
            {
                message = "Không xác định được cửa hàng hiện tại."
            });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new
            {
                message = "Vui lòng chọn file XML."
            });
        }

        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".xml", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Chỉ hỗ trợ file XML."
            });
        }

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        var result = await _inputInvoiceXmlService.UploadXmlAsync(
            _tenantContext.StoreId.Value,
            new UploadInputInvoiceXmlRequest
            {
                StockDocumentId = id,
                OriginalFileName = file.FileName,
                FileBytes = ms.ToArray()
            },
            ct);

        return Ok(new
        {
            message = result.IsExistingInvoice
                ? "XML này đã tồn tại, đã gắn vào phiếu nhập hiện tại."
                : "Đã upload và đọc XML thành công.",
            data = result
        });
    }
    [HttpGet("{id:int}/input-invoices")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> GetInputInvoices(
    int id,
    CancellationToken ct)
    {
        if (!_tenantContext.StoreId.HasValue)
        {
            return BadRequest(new
            {
                message = "Không xác định được cửa hàng hiện tại."
            });
        }

        var result = await _inputInvoiceXmlService.GetInvoicesByStockDocumentAsync(
            _tenantContext.StoreId.Value,
            id,
            ct);

        return Ok(result);
    }
    [HttpGet("{id:int}/input-invoices/line-maps")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> GetInputInvoiceLineMaps(
    int id,
    CancellationToken ct)
    {
        if (!_tenantContext.StoreId.HasValue)
        {
            return BadRequest(new
            {
                message = "Không xác định được cửa hàng hiện tại."
            });
        }

        var result = await _inputInvoiceXmlService.GetLineMapsAsync(
            _tenantContext.StoreId.Value,
            id,
            ct);

        return Ok(result);
    }

    [HttpPost("{id:int}/input-invoices/line-maps")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> UpdateInputInvoiceLineMap(
        int id,
        [FromBody] UpdateStockDocumentLineInputInvoiceMapRequest request,
        CancellationToken ct)
    {
        if (!_tenantContext.StoreId.HasValue)
        {
            return BadRequest(new
            {
                message = "Không xác định được cửa hàng hiện tại."
            });
        }

        try
        {
            await _inputInvoiceXmlService.UpdateLineMapAsync(
                _tenantContext.StoreId.Value,
                id,
                request,
                ct);

            return Ok(new
            {
                message = "Đã cập nhật map dòng XML."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }
    [HttpPost("{id:int}/input-invoices/line-maps/bulk")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> BulkUpdateInputInvoiceLineMaps(
    int id,
    [FromBody] BulkUpdateStockDocumentLineInputInvoiceRequest request,
    CancellationToken ct)
    {
        if (!_tenantContext.StoreId.HasValue)
        {
            return BadRequest(new
            {
                message = "Không xác định được cửa hàng hiện tại."
            });
        }

        try
        {
            await _inputInvoiceXmlService.BulkUpdateLineMapsAsync(
                _tenantContext.StoreId.Value,
                id,
                request.UseInputInvoice,
                ct);

            return Ok(new
            {
                message = request.UseInputInvoice
                    ? "Đã chọn tất cả dòng thuộc hóa đơn XML."
                    : "Đã bỏ chọn tất cả dòng khỏi hóa đơn XML."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }
    /// <summary>
    /// Nhân viên gửi yêu cầu xin sửa phiếu sau khi đã gửi duyệt.
    /// Phiếu vẫn ở trạng thái PendingApproval, quản lý sẽ quyết định có trả về sửa hay không.
    /// </summary>
    [HttpPost("{id:int}/request-revision")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Create)]
    public async Task<IActionResult> RequestRevision(
        int id,
        [FromBody] ApprovalActionRequest? request,
        CancellationToken ct)
    {
        try
        {
            await _stockDocumentService.RequestRevisionAsync(id, request?.Note ?? string.Empty, ct);

            return Ok(new
            {
                message = "Đã gửi yêu cầu sửa phiếu cho quản lý.",
                redirectUrl = Url.Action("Detail", "WarehouseReceiving", new { area = "Admin", id })
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Quản lý xử lý yêu cầu sửa phiếu.
    /// - ReturnToEdit = true: trả phiếu về Rejected để nhân viên sửa.
    /// - ReturnToEdit = false: bỏ qua yêu cầu sửa, phiếu vẫn PendingApproval.
    /// </summary>
    [HttpPost("{id:int}/resolve-revision-request")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> ResolveRevisionRequest(
        int id,
        [FromBody] ResolveRevisionRequest request,
        CancellationToken ct)
    {
        try
        {
            await _stockDocumentService.ResolveRevisionRequestAsync(
                id,
                request.ReturnToEdit,
                request.Note,
                ct);

            return Ok(new
            {
                message = request.ReturnToEdit
                    ? "Đã trả phiếu về cho nhân viên sửa."
                    : "Đã bỏ qua yêu cầu sửa.",
                redirectUrl = Url.Action("Edit", "StockDocumentManagement", new { area = "Admin", id })
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }


}