using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.Models.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/stock-documents")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class StockDocumentManagementController : Controller
{
    private readonly IStockDocumentService _stockDocumentService;
    private readonly IBarcodeLookupService _barcodeLookupService;
    private readonly IAuthorizationService _authorizationService;

    public StockDocumentManagementController(
        IStockDocumentService stockDocumentService,
        IBarcodeLookupService barcodeLookupService,
        IAuthorizationService authorizationService)
    {
        _stockDocumentService = stockDocumentService;
        _barcodeLookupService = barcodeLookupService;
        _authorizationService = authorizationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (!await HasAnyPermissionAsync(
                PermissionCodes.Inventory.StockDocument.View,
                PermissionCodes.Inventory.StockDocument.Update,
                PermissionCodes.Inventory.StockDocument.Approve,
                PermissionCodes.Purchase.Receipt.View,
                PermissionCodes.Purchase.Receipt.Approve))
            return Forbid();

        return View();
    }

    [HttpGet("receipt-form-options")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
    public async Task<IActionResult> GetReceiptFormOptions(CancellationToken ct)
    {
        var result = await _stockDocumentService.GetReceiptFormOptionsAsync(ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var model = await _stockDocumentService.GetDetailAsync(id, ct);
        if (model == null)
            return NotFound();

        var authorized = model.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
            ? await HasAnyPermissionAsync(
                PermissionCodes.Purchase.Receipt.View,
                PermissionCodes.Purchase.Receipt.Update,
                PermissionCodes.Purchase.Receipt.Approve)
            : await HasAnyPermissionAsync(
                PermissionCodes.Inventory.StockDocument.View,
                PermissionCodes.Inventory.StockDocument.Update,
                PermissionCodes.Inventory.StockDocument.Approve);
        if (!authorized)
            return Forbid();

        // Thuế dùng cho bàn duyệt thương mại được nạp cùng request trang.
        // Nhờ vậy người chỉ có quyền duyệt không phải gọi endpoint cấu hình dành
        // cho nhân viên cập nhật phiếu, đồng thời mọi dữ liệu vẫn qua tenant filter.
        var receiptOptions = await _stockDocumentService.GetReceiptFormOptionsAsync(ct);
        ViewData["ReceiptTaxOptions"] = receiptOptions.Taxes;

        return View(model);
    }
    [HttpGet("product-lookup-select2")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
    public async Task<IActionResult> ProductLookupSelect2([FromQuery] string term, CancellationToken ct)
    {
        var canViewCostPrice = await HasAnyPermissionAsync(
            PermissionCodes.Purchase.Order.ViewCost,
            PermissionCodes.Purchase.Receipt.Approve,
            PermissionCodes.Inventory.StockDocument.Approve);
        var items = await _barcodeLookupService.SearchForStockDocumentSelect2Async(term, 20, ct);

        return Json(new
        {
            results = items.Select(x =>
            {
                var factor = NormalizeFactor(x.Factor);
                var defaultUnitCost = canViewCostPrice
                    ? CalculateDefaultPurchaseUnitCost(x.CostPrice, factor)
                    : 0m;

                return new
                {
                    id = $"{x.ProductVariantId}_{x.UnitId ?? 0}",
                    text = x.Text,

                    productVariantId = x.ProductVariantId,
                    unitId = x.UnitId,
                    productName = x.ProductName,
                    sku = x.Sku,
                    barcode = x.Barcode,
                    unitName = x.UnitName,
                    factor = factor,
                    isBaseUnitFallback = x.IsBaseUnitFallback,
                    sourceType = x.SourceType,

                    imageUrl = x.ImageUrl,

                    // Giá bán để tham khảo
                    price = canViewCostPrice ? (decimal?)x.Price : null,

                    // Giá vốn đơn vị gốc
                    costPrice = canViewCostPrice ? x.CostPrice : null,

                    // Giá nhập theo đơn vị đang chọn.
                    // Ví dụ costPrice cái = 7.000, factor lốc = 4 => defaultUnitCost = 28.000
                    defaultUnitCost = defaultUnitCost,

                    canViewCostPrice
                };
            })
        });
    }

    private static decimal NormalizeFactor(decimal? factor)
    {
        var value = factor.GetValueOrDefault(1m);
        return value <= 0 ? 1m : value;
    }

    private static decimal CalculateDefaultPurchaseUnitCost(decimal? baseCostPrice, decimal factor)
    {
        var cost = baseCostPrice.GetValueOrDefault();

        // Trong dữ liệu cũ có trường hợp CostPrice = 1 do mặc định lỗi.
        // Giá nhập 1 đồng gần như không hợp lệ, nên coi như chưa có giá.
        if (cost <= 1m)
            return 0m;

        return Math.Round(cost * factor, 0, MidpointRounding.AwayFromZero);
    }

    [HttpPost("update-header")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
    public async Task<IActionResult> UpdateHeader([FromBody] UpdateStockDocumentHeaderRequest request, CancellationToken ct)
    {
        try
        {
            await _stockDocumentService.UpdateHeaderAsync(request, ct);
            return Json(new { success = true, message = "Cập nhật thông tin phiếu thành công." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("{id:int}/freight")]
    public async Task<IActionResult> UpdateFreight(
        int id,
        [FromForm] UpdatePurchaseReceiptApprovalRequest request,
        CancellationToken ct)
    {
        try
        {
            var document = await _stockDocumentService.GetDetailAsync(id, ct)
                ?? throw new InvalidOperationException("Phiếu nhập kho không tồn tại.");
            var approvePolicy = document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
                ? PermissionCodes.Purchase.Receipt.Approve
                : PermissionCodes.Inventory.StockDocument.Approve;
            if (!(await _authorizationService.AuthorizeAsync(User, null, approvePolicy)).Succeeded)
                return Forbid();
            await _stockDocumentService.UpdatePurchaseReceiptApprovalAsync(id, request, ct);
            TempData["Success"] = request.ResetAutomaticAllocation
                ? "Đã đặt lại phân bổ vận chuyển tự động."
                : "Đã lưu phí và phân bổ vận chuyển.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpGet("lines-table")]
    public async Task<IActionResult> LinesTable(int id, CancellationToken ct)
    {
        var document = await _stockDocumentService.GetDetailAsync(id, ct);
        if (document == null)
            return NotFound();

        var authorized = document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
            ? await HasAnyPermissionAsync(
                PermissionCodes.Purchase.Receipt.View,
                PermissionCodes.Purchase.Receipt.Update,
                PermissionCodes.Purchase.Receipt.Approve)
            : await HasAnyPermissionAsync(
                PermissionCodes.Inventory.StockDocument.View,
                PermissionCodes.Inventory.StockDocument.Update,
                PermissionCodes.Inventory.StockDocument.Approve);
        if (!authorized)
            return Forbid();

        var hasCreatePermission = (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            policyName: AppPermissions.InventoryStockDocumentCreate)).Succeeded;
        var approvePolicy = document.ReceiptSource == PurchaseReceiptSource.PurchaseOrder
            ? PermissionCodes.Purchase.Receipt.Approve
            : PermissionCodes.Inventory.StockDocument.Approve;
        var hasApprovePermission = (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            policyName: approvePolicy)).Succeeded;
        var hasViewCostPermission = (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            policyName: PermissionCodes.Purchase.Order.ViewCost)).Succeeded;

        var vm = new StockDocumentLinesTableViewModel
        {
            CanEdit = hasCreatePermission &&
                      (document.Status == StockDocumentStatus.Draft
                    || document.Status == StockDocumentStatus.PendingApproval
                    || document.Status == StockDocumentStatus.Rejected),

            CanViewCost = hasApprovePermission || hasViewCostPermission,

            Lines = document.Lines?.OrderBy(x => x.LineNo).ToList() ?? new List<StockDocumentLineDto>()
        };

        return PartialView("_StockDocumentLinesTable", vm);
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
}
