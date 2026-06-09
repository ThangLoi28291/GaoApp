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
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.View)]
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var model = await _stockDocumentService.GetDetailAsync(id, ct);
        if (model == null)
            return NotFound();

        return View(model);
    }

    [HttpGet("product-lookup-select2")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Update)]
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
                isBaseUnitFallback = x.IsBaseUnitFallback,
                sourceType = x.SourceType,

                // STOCKDOC.UI:
                // Các field này dùng cho popup xác nhận thêm sản phẩm.
                imageUrl = x.ImageUrl,
                price = x.Price,
                costPrice = x.CostPrice,
                canViewCostPrice = User.IsInRole("ADMIN")
            })
        });
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

    [HttpGet("lines-table")]
    public async Task<IActionResult> LinesTable(int id, CancellationToken ct)
    {
        var document = await _stockDocumentService.GetDetailAsync(id, ct);
        if (document == null)
            return NotFound();

        var hasCreatePermission = (await _authorizationService.AuthorizeAsync(
            User,
            resource: null,
            policyName: AppPermissions.InventoryStockDocumentCreate)).Succeeded;

        var vm = new StockDocumentLinesTableViewModel
        {
            CanEdit = hasCreatePermission &&
                      (document.Status == StockDocumentStatus.Draft
                    || document.Status == StockDocumentStatus.PendingApproval
                    || document.Status == StockDocumentStatus.Rejected),

            Lines = document.Lines?.OrderBy(x => x.LineNo).ToList() ?? new List<StockDocumentLineDto>()
        };

        return PartialView("_StockDocumentLinesTable", vm);
    }
}