using GaoApp.Application.Common.Security;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.Models.Purchases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/purchase-orders")]
[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class PurchaseOrdersController : Controller
{
    private readonly IPurchaseOrderService _service;
    private readonly IProcurementCatalogService _procurementCatalogService;
    private readonly IStockDocumentService _stockDocumentService;
    private readonly IUnitService _unitService;
    private readonly ICurrentStore _currentStore;
    private readonly IAuthorizationService _authorizationService;

    public PurchaseOrdersController(
        IPurchaseOrderService service,
        IProcurementCatalogService procurementCatalogService,
        IStockDocumentService stockDocumentService,
        IUnitService unitService,
        ICurrentStore currentStore,
        IAuthorizationService authorizationService)
    {
        _service = service;
        _procurementCatalogService = procurementCatalogService;
        _stockDocumentService = stockDocumentService;
        _unitService = unitService;
        _currentStore = currentStore;
        _authorizationService = authorizationService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] PurchaseOrderListQueryDto query,
        CancellationToken ct)
    {
        if (!await CanAccessOrdersAsync()) return Forbid();
        var canViewCost = await HasPermissionAsync(PermissionCodes.Purchase.Order.ViewCost);
        var canApprove = await HasPermissionAsync(PermissionCodes.Purchase.Order.Approve);
        if (!canApprove && string.Equals(query.Tab, "awaiting", StringComparison.OrdinalIgnoreCase))
            query.Tab = "all";
        var options = await _service.GetFormOptionsAsync(ct);
        return View(new PurchaseOrderIndexViewModel
        {
            Result = await _service.GetListAsync(query, canViewCost, canApprove, ct),
            LegalEntities = options.LegalEntities,
            CanCreate = await HasPermissionAsync(PermissionCodes.Purchase.Order.Create),
            CanViewCost = canViewCost,
            CanApprove = canApprove
        });
    }

    [HttpGet("create")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Create)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var options = await _service.GetFormOptionsAsync(ct);
        return View("Edit", new PurchaseOrderEditViewModel
        {
            Options = options,
            Request = new SavePurchaseOrderRequest
            {
                LegalEntityId = options.DefaultLegalEntityId ?? 0,
                OrderDate = DateTime.Today,
                Lines = new List<PurchaseOrderLineInputDto>()
            },
            UnitNames = await GetUnitNamesAsync(ct)
        });
    }

    [HttpGet("supplier-lookup")]
    public async Task<IActionResult> SupplierLookup(
        [FromQuery] string? term,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        if (!await CanUseProcurementProductLookupAsync()) return Forbid();

        var result = await _service.SearchSuppliersAsync(term, page, ct);
        return Json(new
        {
            results = result.Items.Select(x => new
            {
                x.Id,
                x.Text,
                x.Code,
                x.Phone,
                x.TaxCode
            }),
            pagination = new { more = result.HasMore }
        });
    }

    [HttpGet("product-lookup")]
    public async Task<IActionResult> ProductLookup(
        [FromQuery] string? term,
        [FromQuery] int? supplierId,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        if (!await CanUseProcurementProductLookupAsync()) return Forbid();

        var result = await _service.SearchProductsAsync(term, supplierId, page, ct);
        return Json(new
        {
            results = result.Items.Select(x => new
            {
                id = x.ProductUnitConversionId,
                text = $"{x.ProductName} · {x.UnitName}",
                x.ProductVariantId,
                x.ProductUnitConversionId,
                x.ProductName,
                x.Sku,
                x.UnitName,
                x.Factor,
                x.Barcode,
                x.ImageUrl,
                x.DefaultTaxId,
                x.DefaultTaxRate,
                x.SuggestedUnitPriceAfterVat,
                x.SupplierId,
                x.SupplierName,
                x.SupplierMatch
            }),
            pagination = new { more = result.HasMore }
        });
    }

    [HttpGet("{id:int}/edit")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Update)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var detail = await _service.GetDetailAsync(id, includeCost: false, ct);
        if (detail == null) return NotFound();
        if (detail.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.ReturnedForRevision))
            return RedirectToAction(nameof(Details), new { id });

        var options = await _service.GetFormOptionsAsync(
            detail.SupplierId,
            detail.Lines.Where(x => x.ProductUnitConversionId.HasValue)
                .Select(x => x.ProductUnitConversionId!.Value).ToArray(),
            ct);
        if (detail.SourcePurchaseRequestId.HasValue)
        {
            return View("CommercialEdit", new PurchaseOrderCommercialEditViewModel
            {
                Order = detail,
                Options = options,
                Request = new UpdateSourcePurchaseOrderCommercialRequest
                {
                    Title = detail.Title,
                    SupplierId = detail.SupplierId,
                    ExpectedWarehouseId = detail.ExpectedWarehouseId,
                    LegalEntityId = detail.LegalEntityId,
                    OrderDate = detail.OrderDate,
                    ExpectedDeliveryDate = detail.ExpectedDeliveryDate,
                    Note = detail.Note,
                    HasVat = false,
                    RowVersion = detail.RowVersion,
                    Lines = detail.Lines.Select(x => new UpdateSourcePurchaseOrderCommercialLineRequest
                    {
                        PurchaseOrderLineId = x.Id,
                        TaxId = 0,
                        UnitPriceBeforeVat = 0m
                    }).ToList()
                }
            });
        }

        return View(new PurchaseOrderEditViewModel
        {
            Options = options,
            UnitNames = await GetUnitNamesAsync(ct),
            OrderNumber = detail.OrderNumber,
            Status = detail.Status,
            Request = new SavePurchaseOrderRequest
            {
                Id = detail.Id, Title = detail.Title, SupplierId = detail.SupplierId, ExpectedWarehouseId = detail.ExpectedWarehouseId,
                LegalEntityId = detail.LegalEntityId, OrderDate = detail.OrderDate,
                ExpectedDeliveryDate = detail.ExpectedDeliveryDate, Note = detail.Note, HasVat = false,
                OutsideRequestReason = detail.OutsideRequestReason,
                RowVersion = detail.RowVersion,
                Lines = detail.Lines.Select(x => new PurchaseOrderLineInputDto
                {
                    Id = x.Id, ItemKind = x.ItemKind, ProductVariantId = x.ProductVariantId,
                    ProductUnitConversionId = x.ProductUnitConversionId, ProductName = x.ProductName,
                    UnitName = x.UnitName, TaxId = 0,
                    Quantity = x.OrderedQuantity, UnitPriceBeforeVat = 0m
                }).ToList()
            }
        });
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save([FromForm] SavePurchaseOrderRequest request, CancellationToken ct)
    {
        var requiredPermission = request.Id.HasValue
            ? PermissionCodes.Purchase.Order.Update
            : PermissionCodes.Purchase.Order.Create;
        if (!(await _authorizationService.AuthorizeAsync(User, null, requiredPermission)).Succeeded)
            return Forbid();
        request.HasVat = false;
        foreach (var line in request.Lines)
        {
            line.TaxId = 0;
            line.UnitPriceBeforeVat = 0m;
        }

        if (!ModelState.IsValid)
            return View("Edit", await BuildEditViewModelAsync(request, ct));

        try
        {
            var id = await _service.SaveAsync(request, ct);
            TempData["Success"] = "Đã lưu đơn đặt hàng.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.SafeMessage);
            return View("Edit", await BuildEditViewModelAsync(request, ct));
        }
    }

    [HttpPost("{id:int}/commercial")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Update)]
    public async Task<IActionResult> UpdateCommercial(
        int id,
        [FromForm] UpdateSourcePurchaseOrderCommercialRequest request,
        CancellationToken ct)
    {
        request.HasVat = false;
        foreach (var line in request.Lines)
        {
            line.TaxId = 0;
            line.UnitPriceBeforeVat = 0m;
        }
        if (!ModelState.IsValid)
            return View("CommercialEdit", await BuildCommercialEditViewModelAsync(id, request, ct));

        try
        {
            await _service.UpdateSourceCommercialAsync(id, request, ct);
            TempData["Success"] = "Đã lưu điều khoản đặt hàng; sản phẩm và số lượng duyệt được giữ nguyên.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError(string.Empty, ex.SafeMessage);
            return View("CommercialEdit", await BuildCommercialEditViewModelAsync(id, request, ct));
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        if (!await CanAccessOrdersAsync()) return Forbid();
        var canViewCost = await HasPermissionAsync(PermissionCodes.Purchase.Order.ViewCost);
        var order = await _service.GetDetailAsync(id, canViewCost, ct);
        if (order == null) return NotFound();

        var currentUserId = GetCurrentUserId();
        var creatorId = order.CreatedByUserId ?? order.Actions
            .Where(x => x.ActionType == PurchaseOrderActionType.Created && x.ActorUserId.HasValue)
            .OrderBy(x => x.OccurredAtUtc)
            .Select(x => x.ActorUserId)
            .FirstOrDefault();
        var isCreatedByCurrentUser = creatorId.HasValue && creatorId == currentUserId;
        var editable = order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.ReturnedForRevision;
        var canUpdate = await HasPermissionAsync(PermissionCodes.Purchase.Order.Update);
        var hasReviewPermission = await HasPermissionAsync(PermissionCodes.Purchase.Order.Approve);
        var canCreateReceipt = order.Status is (PurchaseOrderStatus.Approved or
                                   PurchaseOrderStatus.SentToSupplier or PurchaseOrderStatus.PartiallyReceived) &&
                               await HasPermissionAsync(PermissionCodes.Purchase.Receipt.Create);
        var canResolveItems = canCreateReceipt && order.Lines.Any(x =>
            x.ItemKind == PurchaseItemKind.FreeText &&
            x.PendingQuantity > 0m &&
            (!x.ProductVariantId.HasValue || !x.ProductUnitConversionId.HasValue));
        var canQuickCreateProduct = canResolveItems &&
                                    await HasPermissionAsync(PermissionCodes.Catalog.Product.Create);

        return View(new PurchaseOrderDetailsViewModel
        {
            Order = order,
            CanViewCost = canViewCost,
            CanEdit = editable && canUpdate,
            CanSubmit = editable && canUpdate,
            CanReview = order.Status == PurchaseOrderStatus.PendingApproval &&
                        hasReviewPermission,
            IsCreatedByCurrentUser = isCreatedByCurrentUser,
            CanMarkSent = order.Status == PurchaseOrderStatus.Approved && canUpdate,
            CanCancel = order.Status is not (PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Rejected or
                            PurchaseOrderStatus.FullyReceived or PurchaseOrderStatus.ShortClosed) &&
                        order.Lines.All(x => x.ReceivedQuantity <= 0m) &&
                        await HasPermissionAsync(PermissionCodes.Purchase.Order.Cancel),
            CanCreateReceipt = canCreateReceipt,
            CanPrint = await HasPermissionAsync(PermissionCodes.Purchase.Order.Print),
            CanResolveItems = canResolveItems,
            CanQuickCreateProduct = canQuickCreateProduct,
            CanCreateUnit = canQuickCreateProduct && await HasPermissionAsync(PermissionCodes.Catalog.Unit.Create),
            QuickCreateOptions = canResolveItems
                ? await _procurementCatalogService.GetQuickCreateOptionsAsync(ct)
                : new ProcurementQuickCreateOptionsDto()
        });
    }

    [HttpPost("{id:int}/lines/{lineId:int}/resolve")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Create)]
    public async Task<IActionResult> ResolveLine(
        int id,
        int lineId,
        [FromForm] ResolvePurchaseOrderLineRequest request,
        CancellationToken ct)
    {
        try
        {
            await _procurementCatalogService.ResolvePurchaseOrderLineAsync(id, lineId, request, ct);
            TempData["Success"] = "Đã liên kết hàng mô tả với sản phẩm trong danh mục.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.SafeMessage;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/lines/{lineId:int}/quick-create")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Create)]
    [Authorize(Policy = PermissionCodes.Catalog.Product.Create)]
    public async Task<IActionResult> QuickCreateAndResolveLine(
        int id,
        int lineId,
        [FromForm] QuickCreateAndResolvePurchaseOrderLineRequest request,
        CancellationToken ct)
    {
        try
        {
            var canCreateUnit = await HasPermissionAsync(PermissionCodes.Catalog.Unit.Create);
            var created = await _procurementCatalogService.QuickCreateAndResolvePurchaseOrderLineAsync(
                id,
                lineId,
                request,
                canCreateUnit,
                ct);
            TempData["Success"] =
                $"Đã tạo sản phẩm chờ hoàn thiện '{created.ProductName}' và liên kết với dòng đặt hàng.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.SafeMessage;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:int}/print")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Print)]
    public async Task<IActionResult> Print(
        int id,
        [FromQuery] bool autoPrint = false,
        CancellationToken ct = default)
    {
        var order = await _service.GetDetailAsync(id, includeCost: false, ct);
        return order == null
            ? NotFound()
            : View(new PurchaseOrderPrintViewModel { Order = order, AutoPrint = autoPrint });
    }

    [HttpPost("{id:int}/submit")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Update)]
    public Task<IActionResult> Submit(int id, [FromForm] PurchaseWorkflowRequest request, CancellationToken ct)
        => RunWorkflow(() => _service.SubmitAsync(id, request, ct), id);

    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Approve)]
    public Task<IActionResult> Approve(int id, [FromForm] PurchaseWorkflowRequest request, CancellationToken ct)
        => RunWorkflow(() => _service.ApproveAsync(id, request, ct), id);

    [HttpPost("{id:int}/return")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Approve)]
    public Task<IActionResult> Return(int id, [FromForm] PurchaseWorkflowRequest request, CancellationToken ct)
        => RunWorkflow(() => _service.ReturnForRevisionAsync(id, request, ct), id);

    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Approve)]
    public Task<IActionResult> Reject(int id, [FromForm] PurchaseWorkflowRequest request, CancellationToken ct)
        => RunWorkflow(() => _service.RejectAsync(id, request, ct), id);

    [HttpPost("{id:int}/mark-sent")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Update)]
    public Task<IActionResult> MarkSent(int id, [FromForm] PurchaseWorkflowRequest request, CancellationToken ct)
        => RunWorkflow(() => _service.MarkSentAsync(id, request, ct), id);

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Cancel)]
    public Task<IActionResult> Cancel(int id, [FromForm] PurchaseWorkflowRequest request, CancellationToken ct)
        => RunWorkflow(() => _service.CancelAsync(id, request, ct), id);

    [HttpPost("{id:int}/receipts")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Create)]
    public async Task<IActionResult> CreateReceipt(int id, [FromForm] CreatePurchaseReceiptRequest request, CancellationToken ct)
    {
        try
        {
            var receiptId = await _stockDocumentService.CreateReceiptFromPurchaseOrderAsync(id, request, ct);
            TempData["Success"] = "Đã tạo phiếu nhập từ đơn đặt hàng.";
            return RedirectToAction("Edit", "StockDocumentManagement", new { area = "Admin", id = receiptId });
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.SafeMessage;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    private async Task<IActionResult> RunWorkflow(Func<Task> action, int id)
    {
        try { await action(); TempData["Success"] = "Đã cập nhật trạng thái đơn đặt hàng."; }
        catch (BusinessRuleException ex) { TempData["Error"] = ex.SafeMessage; }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanUseOrderEditorAsync()
    {
        if ((await _authorizationService.AuthorizeAsync(
                User, null, PermissionCodes.Purchase.Order.Create)).Succeeded)
            return true;

        return (await _authorizationService.AuthorizeAsync(
            User, null, PermissionCodes.Purchase.Order.Update)).Succeeded;
    }

    private async Task<bool> CanUseProcurementProductLookupAsync()
    {
        if (await CanUseOrderEditorAsync()) return true;
        return await HasPermissionAsync(PermissionCodes.Purchase.Receipt.Create);
    }

    private async Task<PurchaseOrderEditViewModel> BuildEditViewModelAsync(
        SavePurchaseOrderRequest request,
        CancellationToken ct)
    {
        PurchaseOrderDetailDto? detail = null;
        if (request.Id.HasValue)
            detail = await _service.GetDetailAsync(request.Id.Value, includeCost: false, ct);

        return new PurchaseOrderEditViewModel
        {
            Request = request,
            UnitNames = await GetUnitNamesAsync(ct),
            Options = await _service.GetFormOptionsAsync(
                request.SupplierId > 0 ? request.SupplierId : null,
                request.Lines.Where(x => x.ProductUnitConversionId.HasValue)
                    .Select(x => x.ProductUnitConversionId!.Value).ToArray(),
                ct),
            OrderNumber = detail?.OrderNumber,
            Status = detail?.Status ?? PurchaseOrderStatus.Draft
        };
    }

    private async Task<PurchaseOrderCommercialEditViewModel> BuildCommercialEditViewModelAsync(
        int id,
        UpdateSourcePurchaseOrderCommercialRequest request,
        CancellationToken ct)
    {
        var detail = await _service.GetDetailAsync(id, includeCost: false, ct)
            ?? throw new BusinessRuleException("Đơn đặt hàng không tồn tại.");
        return new PurchaseOrderCommercialEditViewModel
        {
            Order = detail,
            Request = request,
            Options = await _service.GetFormOptionsAsync(
                request.SupplierId > 0 ? request.SupplierId : detail.SupplierId,
                detail.Lines.Where(x => x.ProductUnitConversionId.HasValue)
                    .Select(x => x.ProductUnitConversionId!.Value).ToArray(),
                ct)
        };
    }

    private async Task<bool> HasPermissionAsync(string permission)
        => (await _authorizationService.AuthorizeAsync(User, null, permission)).Succeeded;

    private async Task<bool> CanAccessOrdersAsync()
        => await HasPermissionAsync(PermissionCodes.Purchase.Order.View) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Order.ViewCost) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Order.Create) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Order.Update) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Order.Approve) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Order.Close) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Order.Cancel) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Order.Print) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Receipt.View) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Receipt.Create) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Receipt.Update) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Receipt.Approve) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Receipt.Cancel);

    private async Task<List<string>> GetUnitNamesAsync(CancellationToken ct)
    {
        var units = await _unitService.GetPagedAsync(_currentStore.StoreId, null, 1, 200, ct);
        return units.Items
            .Where(x => x.Status && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private int? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("UserId");
        return int.TryParse(value, out var id) ? id : null;
    }
}
