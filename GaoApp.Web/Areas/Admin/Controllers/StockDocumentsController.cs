using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Inventory.InputInvoices;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Security;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using GaoApp.Application.Common.Exceptions;

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
[AutoValidateAntiforgeryToken]
public class StockDocumentsController : Controller
{
    private readonly IStockDocumentService _stockDocumentService;
    private readonly IBarcodeLookupService _barcodeLookupService;
    private readonly IStockDocumentLookupService _stockDocumentLookupService;
    private readonly ITenantContext _tenantContext;
    private readonly IInputInvoiceXmlService _inputInvoiceXmlService;
    private readonly IInputInvoicePickerService? _inputInvoicePickerService;
    private readonly IAuthorizationService _authorizationService;
    private readonly IStockDocumentSplitService? _stockDocumentSplitService;
    private readonly IInputInvoiceReconciliationService? _inputInvoiceReconciliationService;
    private readonly AppDbContext? _db;
    private readonly IPurchaseReceiptPricingAllocationService? _pricingAllocation;

    public StockDocumentsController(
      IStockDocumentService stockDocumentService,
      IStockDocumentLookupService stockDocumentLookupService,
      IBarcodeLookupService barcodeLookupService,
      ITenantContext tenantContext,
      IInputInvoiceXmlService inputInvoiceXmlService,
      IAuthorizationService authorizationService,
      IInputInvoicePickerService? inputInvoicePickerService = null,
      IStockDocumentSplitService? stockDocumentSplitService = null,
      IInputInvoiceReconciliationService? inputInvoiceReconciliationService = null,
      AppDbContext? db = null,
      IPurchaseReceiptPricingAllocationService? pricingAllocation = null)
    {
        _stockDocumentService = stockDocumentService;
        _stockDocumentLookupService = stockDocumentLookupService;
        _barcodeLookupService = barcodeLookupService;
        _tenantContext = tenantContext;
        _inputInvoiceXmlService = inputInvoiceXmlService;
        _authorizationService = authorizationService;
        _inputInvoicePickerService = inputInvoicePickerService;
        _stockDocumentSplitService = stockDocumentSplitService;
        _inputInvoiceReconciliationService = inputInvoiceReconciliationService;
        _db = db;
        _pricingAllocation = pricingAllocation;
    }


    /// <summary>
    /// Lấy danh sách đơn vị của 1 biến thể sản phẩm.
    /// API này có thể dùng ở màn xem chi tiết nên chỉ cần quyền xem.
    /// </summary>

    [HttpGet("product-variants/{variantId:int}/units")]
    public async Task<IActionResult> GetVariantUnits(
        int variantId,
        [FromQuery] int? stockDocumentId,
        CancellationToken ct)
    {
        if (!await HasMappingLookupPermissionAsync(stockDocumentId, ct))
            return Forbid();
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

        var baseConversion = units.FirstOrDefault(x => x.IsBaseUnit);
        int? baseUnitId = baseConversion?.UnitId;
        string? baseUnitName = baseConversion?.UnitName;
        if (_db is not null)
        {
            var canonicalBaseUnit = await _db.ProductVariants
                .AsNoTracking()
                .Where(x => x.Id == variantId &&
                            x.StoreId == _tenantContext.StoreId)
                .Select(x => new
                {
                    UnitId = x.Product.BaseUnitId,
                    UnitName = x.Product.BaseUnit.Name
                })
                .SingleOrDefaultAsync(ct);
            baseUnitId = canonicalBaseUnit?.UnitId;
            baseUnitName = canonicalBaseUnit?.UnitName;
        }

        return Ok(units.Select(x => new
        {
            productUnitConversionId = x.ProductUnitConversionId,
            unitId = x.UnitId,
            unitName = x.UnitName,
            factor = x.Factor,
            isBaseUnit = x.IsBaseUnit,
            isDefaultForSale = x.IsDefaultForSale,
            baseUnitId,
            baseUnitName
        }));
    }

    /// <summary>
    /// Lấy danh sách phiếu nhập.
    /// Chỉ cần quyền xem chứng từ kho.
    /// </summary>
    [HttpGet("receipts")]
    public async Task<IActionResult> GetReceiptList(CancellationToken ct)
    {
        if (!await HasAnyPermissionAsync(
                PermissionCodes.Inventory.StockDocument.View,
                PermissionCodes.Inventory.StockDocument.Update,
                PermissionCodes.Inventory.StockDocument.Approve,
                PermissionCodes.Purchase.Receipt.View,
                PermissionCodes.Purchase.Receipt.Approve))
            return Forbid();

        var result = await _stockDocumentService.GetReceiptListAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Danh sách HKD active và các kho active thuộc từng HKD dùng cho form nhập kho.
    /// HKD mặc định lấy theo IsDefaultForPurchase.
    /// </summary>
    [HttpGet("receipt-form-options")]
    public async Task<IActionResult> GetReceiptFormOptions(CancellationToken ct)
    {
        var allowedPolicies = new[]
        {
            PermissionCodes.Inventory.StockDocument.Create,
            PermissionCodes.Inventory.StockDocument.Update,
            PermissionCodes.Inventory.StockDocument.Approve,
            PermissionCodes.Purchase.Receipt.Approve
        };
        var authorized = false;
        foreach (var policy in allowedPolicies)
        {
            if ((await _authorizationService.AuthorizeAsync(User, null, policy)).Succeeded)
            {
                authorized = true;
                break;
            }
        }
        if (!authorized) return Forbid();

        var result = await _stockDocumentService.GetReceiptFormOptionsAsync(ct);
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

        var canApprove = await HasWorkflowPermissionAsync(
            id,
            PermissionCodes.Purchase.Receipt.Approve,
            PermissionCodes.Inventory.StockDocument.Approve,
            ct);
        var canViewCost = canApprove || (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            policyName: PermissionCodes.Purchase.Order.ViewCost)).Succeeded;
        if (!canViewCost)
            RedactCommercialValues(result);

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
    public async Task<IActionResult> SearchProducts(
        [FromQuery] string keyword,
        [FromQuery] int? stockDocumentId,
        CancellationToken ct)
    {
        if (!await HasMappingLookupPermissionAsync(stockDocumentId, ct))
            return Forbid();
        // Lookup chứng từ kho phải thấy cả sản phẩm đang chờ hoàn thiện
        // (IsSellable = false); chỉ POS mới được phép lọc theo IsSellable.
        var result = await _barcodeLookupService.SearchForStockDocumentSelect2Async(keyword, 20, ct);
        return Ok(result);
    }

    [HttpGet("{id:int}/audit-events")]
    [Authorize(Policy = PermissionCodes.System.AuditLog.View)]
    public async Task<IActionResult> GetAuditTimeline(
        int id,
        CancellationToken ct)
    {
        var result = await _stockDocumentService.GetAuditTimelineAsync(id, ct);
        if (result is null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy phiếu nhập kho."
            });
        }

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
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
        {
            request.UnitCost = 0m;
            request.TaxId = null;
        }

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
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
        {
            request.UnitCost = 0m;
            request.TaxId = null;
        }

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
        if (!await HasWorkflowPermissionAsync(
                documentId,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
        {
            request.UnitCost = null;
            request.TaxId = null;
        }

        await _stockDocumentService.UpdateLineAsync(lineId, request, ct);

        return Ok(new
        {
            message = "Cập nhật dòng phiếu nhập kho thành công.",
            rowVersion = _db == null ? null : Convert.ToBase64String((await _db.StockDocuments.SingleAsync(x => x.Id == documentId, ct)).RowVersion),
            lineRowVersion = _db == null ? null : Convert.ToBase64String((await _db.StockDocumentLines.SingleAsync(x => x.Id == lineId && x.StockDocumentId == documentId, ct)).RowVersion)
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
        var document = await _stockDocumentService.GetDetailAsync(documentId, ct);
        if (document is null || !document.Lines.Any(x => x.Id == lineId))
            return NotFound(new { message = "Dòng hàng không còn trong phiếu nhập này. Vui lòng tải lại danh sách." });
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
    public async Task<IActionResult> SubmitApproval(
        int id,
        [FromBody] ApprovalActionRequest? request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Update,
                PermissionCodes.Inventory.StockDocument.Create,
                ct))
            return Forbid();
        await _stockDocumentService.SubmitForApprovalAsync(id, request?.Note, request?.RowVersion, ct);

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
    public async Task<IActionResult> Approve(
        int id,
        [FromBody] ApprovalActionRequest? request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
            return Forbid();

        var document = await _stockDocumentService.GetDetailAsync(id, ct);
        if (document == null)
            return NotFound(new { message = "Không tìm thấy phiếu nhập kho." });
        if (document.Status == StockDocumentStatus.PendingApproval)
        {
            return BadRequest(new
            {
                message = "Phiếu chờ duyệt phải được chốt giá/VAT/công nợ tại màn duyệt thương mại."
            });
        }

        await _stockDocumentService.ApproveAsync(id, request?.Note, request?.RowVersion, ct);

        return Ok(new
        {
            message = "Đã duyệt phiếu nhập kho thành công.",
            redirectUrl = Url.Action("Index", "StockDocumentManagement", new { area = "Admin" })
        });
    }

    /// <summary>
    /// Chốt giá/VAT/công nợ/phí vận chuyển và duyệt phiếu trong một transaction.
    /// Không nhận sản phẩm, đơn vị hoặc số lượng từ trình duyệt.
    /// </summary>
    [HttpGet("{id:int}/pricing-allocation")]
    [RequireAnyPermission(PermissionCodes.Purchase.Receipt.Approve, PermissionCodes.Inventory.StockDocument.Approve)]
    public Task<IActionResult> GetPricingAllocation(int id, CancellationToken ct)
        => PricingAllocationAction(id, service => service.GetAsync(id, ct), ct);

    [HttpPost("{id:int}/pricing-allocation/preview")]
    [RequireAnyPermission(PermissionCodes.Purchase.Receipt.Approve, PermissionCodes.Inventory.StockDocument.Approve)]
    public Task<IActionResult> PreviewPricingAllocation(int id, [FromBody] PurchaseReceiptPricingAllocationRequest request, CancellationToken ct)
        => PricingAllocationAction(id, service => service.PreviewAsync(id, request, ct), ct);

    [HttpPut("{id:int}/pricing-allocation")]
    [RequireAnyPermission(PermissionCodes.Purchase.Receipt.Approve, PermissionCodes.Inventory.StockDocument.Approve)]
    public Task<IActionResult> SavePricingAllocation(int id, [FromBody] PurchaseReceiptPricingAllocationRequest request, CancellationToken ct)
        => PricingAllocationAction(id, service => service.SaveAsync(id, request, ct), ct);

    [HttpPost("{id:int}/pricing-allocation/apply")]
    [RequireAnyPermission(PermissionCodes.Purchase.Receipt.Approve, PermissionCodes.Inventory.StockDocument.Approve)]
    public Task<IActionResult> ApplyPricingAllocation(int id, [FromBody] PurchaseReceiptPricingApplyRequest request, CancellationToken ct)
        => PricingAllocationAction(id, service => service.ApplyAsync(id, request, ct), ct);

    private async Task<IActionResult> PricingAllocationAction<T>(int id,
        Func<IPurchaseReceiptPricingAllocationService, Task<T>> action, CancellationToken ct)
    {
        var document = await _stockDocumentService.GetDetailAsync(id, ct);
        if (document is null) return NotFound(new { message = "Không tìm thấy phiếu nhập." });
        var authorized = (await _authorizationService.AuthorizeAsync(User, null, PermissionCodes.Purchase.Receipt.Approve)).Succeeded ||
            (await _authorizationService.AuthorizeAsync(User, null, PermissionCodes.Inventory.StockDocument.Approve)).Succeeded;
        if (!authorized) return Forbid();
        if (_pricingAllocation is null) return StatusCode(503, new { message = "Chức năng kế hoạch giá chưa sẵn sàng." });
        try { return Ok(await action(_pricingAllocation)); }
        catch (PurchaseReceiptPricingConflictException ex) { return Conflict(new { message = ex.SafeMessage }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Phiếu hoặc kế hoạch vừa thay đổi. Vui lòng tải lại và đối chiếu." }); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627 or 1205)
        { return Conflict(new { message = "Kế hoạch vừa được cập nhật ở nơi khác. Vui lòng tải lại và đối chiếu." }); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205)
        { return Conflict(new { message = "Có cập nhật đồng thời. Vui lòng tải lại và thử lại." }); }
        catch (BusinessRuleException ex) { return BadRequest(new { message = ex.SafeMessage }); }
        catch (OverflowException) { return BadRequest(new { message = "Giá hoặc thành tiền vượt giới hạn cho phép." }); }
    }

    [HttpPost("{id:int}/price-draft")]
    [RequireAnyPermission(PermissionCodes.Purchase.Receipt.Approve, PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> SavePriceDraft(int id, [FromBody] SaveReceiptPriceDraftRequest request, CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(new { message = "Giá nháp không hợp lệ. Giá phải lớn hơn 0 và nằm trong giới hạn cho phép." });
        try { return Ok(await _stockDocumentService.SavePriceDraftAsync(id, request, ct)); }
        catch (PurchaseReceiptPricingConflictException ex) { return Conflict(new { message = ex.SafeMessage }); }
        catch (BusinessRuleException ex) { return BadRequest(new { message = ex.SafeMessage }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Phiếu vừa được thay đổi ở nơi khác. Giá đang nhập chưa được lưu; hãy đối chiếu trước khi tải lại." }); }
        catch (OverflowException) { return BadRequest(new { message = "Giá hoặc thành tiền vượt giới hạn cho phép." }); }
    }

    [HttpPost("{id:int}/approve-commercial")]
    public async Task<IActionResult> ApproveCommercial(
        int id,
        [FromBody] ApprovePurchaseReceiptCommercialRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
            return Forbid();

        try
        {
            await _stockDocumentService.ApproveCommercialAsync(id, request, ct);
            return Ok(new
            {
                message = "Đã chốt giá và duyệt phiếu nhập kho thành công.",
                redirectUrl = Url.Action("Index", "StockDocumentManagement", new { area = "Admin" })
            });
        }
        catch (PurchaseReceiptPricingConflictException ex)
        {
            return Conflict(new { message = ex.SafeMessage });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205)
        {
            return Conflict(new { message = "Có cập nhật đồng thời khi duyệt. Vui lòng tải lại và đối chiếu trước khi thử lại." });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { message = ex.SafeMessage });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                message = "Phiếu đã được người khác thay đổi. Vui lòng tải lại trước khi duyệt."
            });
        }
        catch (OverflowException)
        {
            return BadRequest(new
            {
                message = "Giá trị tiền hoặc số lượng vượt giới hạn cho phép."
            });
        }
    }

    /// <summary>
    /// Trả phiếu nhập kho về để chỉnh sửa.
    /// Cần quyền duyệt chứng từ kho.
    /// </summary>
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] ApprovalActionRequest? request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
            return Forbid();
        await _stockDocumentService.RejectAsync(id, request?.Note, request?.RowVersion, ct);

        return Ok(new
        {
            message = "Đã trả phiếu về để chỉnh sửa.",
            redirectUrl = Url.Action("Index", "StockDocumentManagement", new { area = "Admin" })
        });
    }

    private async Task<bool> HasWorkflowPermissionAsync(
        int documentId,
        string purchaseReceiptPolicy,
        string directReceiptPolicy,
        CancellationToken ct)
    {
        var document = await _stockDocumentService.GetDetailAsync(documentId, ct);
        if (document == null) return false;
        var policy = document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
            ? purchaseReceiptPolicy
            : directReceiptPolicy;
        return (await _authorizationService.AuthorizeAsync(User, null, policy)).Succeeded;
    }

    private async Task<bool> HasMappingLookupPermissionAsync(
        int? stockDocumentId,
        CancellationToken ct)
    {
        if ((await _authorizationService.AuthorizeAsync(
                User,
                resource: null,
                policyName: PermissionCodes.Inventory.StockDocument.Create)).Succeeded)
            return true;

        return stockDocumentId is > 0 && await HasWorkflowPermissionAsync(
            stockDocumentId.Value,
            PermissionCodes.Purchase.Receipt.Approve,
            PermissionCodes.Inventory.StockDocument.Approve,
            ct);
    }

    private IInputInvoicePickerService RequirePicker() =>
        _inputInvoicePickerService
        ?? throw new InvalidOperationException("Input-invoice picker is not configured.");

    private IStockDocumentSplitService RequireSplitService() =>
        _stockDocumentSplitService
        ?? throw new InvalidOperationException("Purchase-receipt split service is not configured.");

    [HttpPost("{id:int}/split")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> SplitReceipt(
        int id,
        [FromBody] PurchaseReceiptSplitRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await RequireSplitService().SplitAsync(id, request, ct);
            return Ok(new
            {
                message = "Đã tách phiếu nhập.",
                resultLinks = result.Results
            });
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("split/invoice-picker/browse")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> BrowseSplitInvoicePicker(
        [FromQuery] int supplierId,
        [FromQuery] int warehouseId,
        [FromQuery] InputInvoicePickerBrowseRequest request,
        CancellationToken ct)
    {
        // BrowseForSupplierAsync remains the supplier-only compatibility contract;
        // split adds Warehouse owner context through the same existing route.
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            return Ok(await RequirePicker().BrowseForSupplierAndWarehouseAsync(
                _tenantContext.StoreId.Value, supplierId, warehouseId, request, ct));
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("split/invoice-picker/pdf")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> PreviewSplitInvoicePdf(
        [FromQuery] int supplierId,
        [FromQuery] string documentKey,
        CancellationToken ct)
    {
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            var preview = await RequirePicker().GetPdfForSupplierAsync(
                _tenantContext.StoreId.Value, supplierId, documentKey, ct);
            return File(preview.Content, "application/pdf", enableRangeProcessing: true);
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("split/invoice-picker/xml")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> PreviewSplitInvoiceXml(
        [FromQuery] int supplierId,
        [FromQuery] int warehouseId,
        [FromQuery] string documentKey,
        CancellationToken ct)
    {
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            // GetXmlPreviewForSupplierAsync remains the supplier-only compatibility contract;
            // split preview adds Warehouse owner context through the same existing route.
            var preview = await RequirePicker().GetXmlPreviewForSupplierAndWarehouseAsync(
                _tenantContext.StoreId.Value, supplierId, warehouseId, documentKey, ct);
            return PartialView(
                "~/Areas/Admin/Views/StockDocumentManagement/_InputInvoiceXmlPreview.cshtml",
                preview);
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }
    [HttpGet("{id:int}/input-invoices/picker/candidates")]
    public async Task<IActionResult> BrowseInputInvoicePicker(
        int id,
        [FromQuery] InputInvoicePickerBrowseRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            var result = await RequirePicker().BrowseAsync(
                _tenantContext.StoreId.Value, id, request, ct);
            return Ok(result);
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("{id:int}/input-invoices/association")]
    public async Task<IActionResult> GetInputInvoiceAssociation(
        int id,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (_tenantContext.StoreId is not > 0)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            return Ok(await RequirePicker().GetAssociationContextAsync(
                _tenantContext.StoreId.Value, id, ct));
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("{id:int}/input-invoices/picker/pdf")]
    public async Task<IActionResult> PreviewInputInvoicePdf(
        int id, [FromQuery] string documentKey, CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            var preview = await RequirePicker().GetPdfAsync(
                _tenantContext.StoreId.Value, id, documentKey, ct);
            return File(preview.Content, "application/pdf", enableRangeProcessing: true);
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("{id:int}/input-invoices/picker/xml")]
    public async Task<IActionResult> PreviewInputInvoiceXml(
        int id, [FromQuery] string documentKey, CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            var preview = await RequirePicker().GetXmlPreviewAsync(
                _tenantContext.StoreId.Value, id, documentKey, ct);
            return PartialView(
                "~/Areas/Admin/Views/StockDocumentManagement/_InputInvoiceXmlPreview.cshtml",
                preview);
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpPost("{id:int}/input-invoices/picker/select")]
    public async Task<IActionResult> SelectInputInvoiceFromPicker(
        int id,
        [FromBody] SelectInputInvoiceDocumentRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            var result = await RequirePicker().SelectAsync(
                _tenantContext.StoreId.Value, id, request, ct);
            return Ok(new { message = "Đã liên kết hóa đơn với phiếu nhập.", data = result });
        }
        catch (InputInvoiceAssociationException exception)
        {
            return Conflict(new
            {
                code = exception.Code,
                message = exception.SafeMessage,
                currentInputInvoiceHeadId = exception.CurrentInputInvoiceHeadId
            });
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("{id:int}/input-invoices/{inputInvoiceId:int}/preview/pdf")]
    public async Task<IActionResult> PreviewLinkedInputInvoicePdf(
        int id,
        int inputInvoiceId,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            var preview = await RequirePicker().GetLinkedPdfAsync(
                _tenantContext.StoreId.Value, id, inputInvoiceId, ct);
            return File(preview.Content, "application/pdf", enableRangeProcessing: true);
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("{id:int}/input-invoices/{inputInvoiceId:int}/preview/xml")]
    public async Task<IActionResult> PreviewLinkedInputInvoiceXml(
        int id,
        int inputInvoiceId,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            var preview = await RequirePicker().GetLinkedXmlPreviewAsync(
                _tenantContext.StoreId.Value, id, inputInvoiceId, ct);
            return PartialView(
                "~/Areas/Admin/Views/StockDocumentManagement/_InputInvoiceXmlPreview.cshtml",
                preview);
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpPost("{id:int}/input-invoices/{inputInvoiceId:int}/unlink")]
    public async Task<IActionResult> UnlinkInputInvoice(
        int id,
        int inputInvoiceId,
        [FromBody] UnlinkInputInvoiceRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (!_tenantContext.StoreId.HasValue)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            if (request.ExpectedCurrentInputInvoiceHeadId <= 0)
                request.ExpectedCurrentInputInvoiceHeadId = inputInvoiceId;
            if (request.ExpectedCurrentInputInvoiceHeadId != inputInvoiceId)
                return Conflict(new
                {
                    code = InputInvoiceAssociationException.AssociationChangedCode,
                    message = "Hóa đơn hiện tại không khớp yêu cầu. Vui lòng tải lại."
                });
            var result = await RequirePicker().UnlinkAsync(
                _tenantContext.StoreId.Value, id, request, ct);
            return Ok(new
            {
                message = result.Outcome == InputInvoiceAssociationMutationOutcomes.Applied
                    ? "Đã gỡ liên kết hóa đơn khỏi phiếu nhập."
                    : "Hóa đơn đã được gỡ liên kết trước đó.",
                data = result
            });
        }
        catch (InputInvoiceAssociationException exception)
        {
            return Conflict(new
            {
                code = exception.Code,
                message = exception.SafeMessage,
                currentInputInvoiceHeadId = exception.CurrentInputInvoiceHeadId
            });
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpPost("{id:int}/input-invoices/relink")]
    public async Task<IActionResult> RelinkInputInvoice(
        int id,
        [FromBody] RelinkInputInvoiceRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id, PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (_tenantContext.StoreId is not > 0)
            return BadRequest(new { message = "Không xác định được cửa hàng hiện tại." });
        try
        {
            var result = await RequirePicker().RelinkAsync(
                _tenantContext.StoreId.Value, id, request, ct);
            return Ok(new
            {
                message = result.Outcome == InputInvoiceAssociationMutationOutcomes.Applied
                    ? "Đã thay hóa đơn liên kết."
                    : "Hóa đơn thay thế đã được áp dụng trước đó.",
                data = result
            });
        }
        catch (InputInvoiceAssociationException exception)
        {
            return Conflict(new
            {
                code = exception.Code,
                message = exception.SafeMessage,
                currentInputInvoiceHeadId = exception.CurrentInputInvoiceHeadId
            });
        }
        catch (BusinessRuleException exception)
        {
            return BadRequest(new { message = exception.SafeMessage });
        }
    }

    [HttpGet("{id:int}/input-invoices")]
    public async Task<IActionResult> GetInputInvoices(
    int id,
    CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
            return Forbid();

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
    public async Task<IActionResult> GetInputInvoiceLineMaps(
    int id,
    CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
            return Forbid();

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
    public async Task<IActionResult> UpdateInputInvoiceLineMap(
        int id,
        [FromBody] UpdateStockDocumentLineInputInvoiceMapRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
            return Forbid();

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
        catch (BusinessRuleException ex)
        {
            return BadRequest(new
            {
                message = ex.SafeMessage
            });
        }
    }
    [HttpPost("{id:int}/input-invoices/line-maps/bulk")]
    public async Task<IActionResult> BulkUpdateInputInvoiceLineMaps(
    int id,
    [FromBody] BulkUpdateStockDocumentLineInputInvoiceRequest request,
    CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
            return Forbid();

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
                request.ExclusionReason,
                ct);

            return Ok(new
            {
                message = request.UseInputInvoice
                    ? "Đã chọn tất cả dòng thuộc hóa đơn XML."
                    : "Đã bỏ chọn tất cả dòng khỏi hóa đơn XML."
            });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new
            {
                message = ex.SafeMessage
            });
        }
    }

    [HttpGet("{id:int}/input-invoices/reconciliation")]
    public async Task<IActionResult> GetInputInvoiceReconciliation(
        int id, CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (_tenantContext.StoreId is not > 0 ||
            _inputInvoiceReconciliationService is null)
            return BadRequest(new { message = "Dịch vụ đối chiếu chưa sẵn sàng." });
        try
        {
            return Ok(await _inputInvoiceReconciliationService.GetForReceiptAsync(
                _tenantContext.StoreId.Value, id, ct));
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { message = ex.SafeMessage });
        }
    }

    [HttpPost("{id:int}/input-invoices/reconciliation/preview")]
    public async Task<IActionResult> PreviewInputInvoiceReconciliation(
        int id, [FromBody] InputInvoiceCommercialPreviewRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (_tenantContext.StoreId is not > 0 ||
            _inputInvoiceReconciliationService is null)
            return BadRequest(new { message = "Dịch vụ đối chiếu chưa sẵn sàng." });
        try
        {
            return Ok(await _inputInvoiceReconciliationService.PreviewCommercialAsync(
                _tenantContext.StoreId.Value, id, request, ct));
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { message = ex.SafeMessage });
        }
    }

    [HttpPost("{id:int}/input-invoices/reconciliation/details/{detailId:int}/ignore")]
    public Task<IActionResult> IgnoreInputInvoiceDetail(
        int id, int detailId,
        [FromBody] InputInvoiceReconciliationReasonRequest request,
        CancellationToken ct)
        => SetInputInvoiceDetailIgnored(id, detailId, true, request, ct);

    [HttpPost("{id:int}/input-invoices/reconciliation/details/{detailId:int}/unignore")]
    public Task<IActionResult> UnignoreInputInvoiceDetail(
        int id, int detailId,
        [FromBody] InputInvoiceReconciliationReasonRequest request,
        CancellationToken ct)
        => SetInputInvoiceDetailIgnored(id, detailId, false, request, ct);

    [HttpPost("{id:int}/input-invoices/reconciliation/accept")]
    public async Task<IActionResult> AcceptInputInvoiceReconciliation(
        int id, [FromBody] AcceptInputInvoiceReconciliationRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (_tenantContext.StoreId is not > 0 ||
            _inputInvoiceReconciliationService is null)
            return BadRequest(new { message = "Dịch vụ đối chiếu chưa sẵn sàng." });
        try
        {
            return Ok(await _inputInvoiceReconciliationService
                .AcceptMismatchWithinTransactionAsync(
                    _tenantContext.StoreId.Value, id, request.Reason,
                    request.ExpectedEvidenceFingerprint, ct));
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { message = ex.SafeMessage });
        }
    }

    private async Task<IActionResult> SetInputInvoiceDetailIgnored(
        int id, int detailId, bool ignored,
        InputInvoiceReconciliationReasonRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve, ct))
            return Forbid();
        if (_tenantContext.StoreId is not > 0 ||
            _inputInvoiceReconciliationService is null)
            return BadRequest(new { message = "Dịch vụ đối chiếu chưa sẵn sàng." });
        try
        {
            return Ok(await _inputInvoiceReconciliationService
                .IgnoreXmlDetailWithinTransactionAsync(
                    _tenantContext.StoreId.Value, id, detailId, ignored,
                    request.Reason, ct));
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { message = ex.SafeMessage });
        }
    }
    /// <summary>
    /// Nhân viên gửi yêu cầu xin sửa phiếu sau khi đã gửi duyệt.
    /// Phiếu vẫn ở trạng thái PendingApproval, quản lý sẽ quyết định có trả về sửa hay không.
    /// </summary>
    [HttpPost("{id:int}/request-revision")]
    public async Task<IActionResult> RequestRevision(
        int id,
        [FromBody] ApprovalActionRequest? request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Update,
                PermissionCodes.Inventory.StockDocument.Update,
                ct))
            return Forbid();

        try
        {
            await _stockDocumentService.RequestRevisionAsync(
                id,
                request?.Note ?? string.Empty,
                request?.RowVersion,
                ct);

            return Ok(new
            {
                message = "Đã gửi yêu cầu sửa phiếu cho quản lý.",
                redirectUrl = Url.Action("Detail", "WarehouseReceiving", new { area = "Admin", id })
            });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new
            {
                message = ex.SafeMessage
            });
        }
    }

    /// <summary>
    /// Quản lý xử lý yêu cầu sửa phiếu.
    /// - ReturnToEdit = true: trả phiếu về Rejected để nhân viên sửa.
    /// - ReturnToEdit = false: bỏ qua yêu cầu sửa, phiếu vẫn PendingApproval.
    /// </summary>
    [HttpPost("{id:int}/resolve-revision-request")]
    public async Task<IActionResult> ResolveRevisionRequest(
        int id,
        [FromBody] ResolveRevisionRequest request,
        CancellationToken ct)
    {
        if (!await HasWorkflowPermissionAsync(
                id,
                PermissionCodes.Purchase.Receipt.Approve,
                PermissionCodes.Inventory.StockDocument.Approve,
                ct))
            return Forbid();

        try
        {
            await _stockDocumentService.ResolveRevisionRequestAsync(
                id,
                request.ReturnToEdit,
                request.RowVersion,
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
        catch (BusinessRuleException ex)
        {
            return BadRequest(new
            {
                message = ex.SafeMessage
            });
        }
    }

    private async Task<bool> HasAnyPermissionAsync(params string[] policies)
    {
        foreach (var policy in policies)
        {
            if ((await _authorizationService.AuthorizeAsync(User, null, policy)).Succeeded)
                return true;
        }

        return false;
    }

    private static void RedactCommercialValues(StockDocumentDto document)
    {
        document.TotalAmount = 0m;
        document.SubtotalBeforeVat = 0m;
        document.VatAmount = 0m;
        document.FreightTotal = 0m;

        foreach (var line in document.Lines)
        {
            line.UnitCost = 0m;
            line.LineTotal = 0m;
            line.TaxId = null;
            line.TaxNameSnapshot = null;
            line.UnitPriceBeforeVat = 0m;
            line.LastPurchaseUnitPriceBeforeVat = null;
            line.TaxRate = 0m;
            line.VatAmount = 0m;
            line.UnitPriceAfterVat = 0m;
            line.FreightAllocation = 0m;
            line.AmountDifference = 0m;
        }
    }


}
