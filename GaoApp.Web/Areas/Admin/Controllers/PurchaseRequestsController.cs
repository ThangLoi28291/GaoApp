using System.Security.Claims;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Services.Purchases;
using GaoApp.Application.Interfaces.Services.Units;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.Models.Purchases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/purchase-requests")]
[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class PurchaseRequestsController : Controller
{
    private readonly IPurchaseRequestService _service;
    private readonly IPurchaseOrderService _purchaseOrderService;
    private readonly IUnitService _unitService;
    private readonly ICurrentStore _currentStore;
    private readonly IAuthorizationService _authorization;

    public PurchaseRequestsController(
        IPurchaseRequestService service,
        IPurchaseOrderService purchaseOrderService,
        IUnitService unitService,
        ICurrentStore currentStore,
        IAuthorizationService authorization)
    {
        _service = service;
        _purchaseOrderService = purchaseOrderService;
        _unitService = unitService;
        _currentStore = currentStore;
        _authorization = authorization;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] bool mine = false, CancellationToken ct = default)
    {
        var canViewStore = await HasPermissionAsync(PermissionCodes.Purchase.Request.ViewStore);
        var canViewOwn = await HasPermissionAsync(PermissionCodes.Purchase.Request.ViewOwn);
        if (!canViewStore && !canViewOwn) return Forbid();

        var onlyMine = !canViewStore || mine;
        return View(new PurchaseRequestIndexViewModel
        {
            Items = await _service.GetListAsync(onlyMine, ct),
            CanCreate = await HasPermissionAsync(PermissionCodes.Purchase.Request.Create),
            CanViewStore = canViewStore,
            ShowingOnlyMine = onlyMine
        });
    }

    [HttpGet("create")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.Create)]
    public async Task<IActionResult> Create(CancellationToken ct)
        => View("Edit", new PurchaseRequestEditViewModel
        {
            Request = new SavePurchaseRequestRequest
            {
                RequestDate = DateTime.Today,
                Lines = new List<PurchaseRequestLineInputDto>()
            },
            CanSubmit = await HasPermissionAsync(PermissionCodes.Purchase.Request.Submit),
            UnitNames = await GetUnitNamesAsync(ct)
        });

    [HttpGet("{id:int}/edit")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.UpdateOwn)]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var detail = await _service.GetDetailAsync(id, requireOwnership: true, ct);
        if (detail == null) return NotFound();
        if (detail.Status is not (PurchaseRequestStatus.Draft or PurchaseRequestStatus.ReturnedForRevision))
            return RedirectToAction(nameof(Details), new { id });

        var request = new SavePurchaseRequestRequest
        {
            Id = detail.Id,
            Title = detail.Title,
            RequestDate = detail.RequestDate,
            NeedByDate = detail.NeedByDate,
            Note = detail.Note,
            RowVersion = detail.RowVersion,
            Lines = detail.Lines.Select(x => new PurchaseRequestLineInputDto
            {
                Id = x.Id,
                ItemKind = x.ItemKind,
                ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.ProductUnitConversionId,
                ProductName = x.ProductName,
                UnitName = x.UnitName,
                Quantity = x.RequestedQuantity
            }).ToList()
        };

        return View(await BuildEditViewModelAsync(request, detail, ct));
    }

    [HttpGet("product-lookup")]
    public async Task<IActionResult> ProductLookup(
        [FromQuery] string? term,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        if (!await CanUseEditorAsync()) return Forbid();

        var result = await _service.SearchProductsAsync(term, page, ct);
        return Json(new
        {
            results = result.Items.Select(x => new
            {
                id = x.ProductUnitConversionId,
                text = x.Text,
                x.ProductVariantId,
                x.ProductUnitConversionId,
                x.UnitId,
                x.Sku,
                x.UnitName,
                x.Factor,
                x.Barcode,
                x.ImageUrl,
                x.CurrentStockBaseQuantity,
                x.IncomingBaseQuantity,
                x.HasOpenRequest
            }),
            pagination = new { more = result.HasMore }
        });
    }

    [HttpGet("supplier-lookup")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.Convert)]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Create)]
    public async Task<IActionResult> SupplierLookup(
        [FromQuery] string? term,
        [FromQuery] int page = 1,
        CancellationToken ct = default)
    {
        var result = await _purchaseOrderService.SearchSuppliersAsync(term, page, ct);
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

    [HttpGet("{id:int}/prepare")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.Convert)]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Create)]
    public async Task<IActionResult> Prepare(int id, CancellationToken ct)
    {
        try
        {
            var preparation = await _service.GetPreparationAsync(id, includeCost: false, ct);
            if (preparation.Lines.Count == 0)
            {
                TempData["Error"] = "Yêu cầu mua không còn số lượng đã duyệt cần lập đơn.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var request = BuildDefaultConversionRequest(preparation);
            return View(BuildPrepareViewModel(preparation, request, null));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost("{id:int}/convert")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionCodes.Purchase.Request.Convert)]
    [Authorize(Policy = PermissionCodes.Purchase.Order.Create)]
    public async Task<IActionResult> Convert(
        int id,
        [FromForm] ConvertPurchaseRequestRequest request,
        [FromForm] string? supplierLabel,
        CancellationToken ct)
    {
        request.Orders ??= new List<PurchaseRequestOrderGroupRequest>();
        if (request.Orders.Count != 1)
            ModelState.AddModelError(nameof(request.Orders),
                "Mỗi yêu cầu mua chỉ được lập thành một đơn đặt hàng.");

        if (!ModelState.IsValid)
            return await RenderPrepareAsync(id, request, supplierLabel, ct);

        try
        {
            await _service.ConvertAsync(id, request, ct);
            TempData["Success"] = "Đã tạo một đơn đặt hàng nháp từ yêu cầu mua.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await RenderPrepareAsync(id, request, supplierLabel, ct);
        }
    }

    [HttpPost("save")]
    public async Task<IActionResult> Save(
        [FromForm] SavePurchaseRequestRequest request,
        [FromForm] string? intent,
        CancellationToken ct)
    {
        var requiredPermission = request.Id.HasValue
            ? PermissionCodes.Purchase.Request.UpdateOwn
            : PermissionCodes.Purchase.Request.Create;
        if (!await HasPermissionAsync(requiredPermission)) return Forbid();

        var submitAfterSave = string.Equals(intent, "submit", StringComparison.OrdinalIgnoreCase);
        if (submitAfterSave && !await HasPermissionAsync(PermissionCodes.Purchase.Request.Submit))
            return Forbid();

        // RequestDate/NeedByDate are not editable on the employee form. Never trust
        // hidden/browser values for fields that the current workflow does not expose.
        if (request.Id.HasValue)
        {
            var current = await _service.GetDetailAsync(request.Id.Value, requireOwnership: true, ct);
            if (current == null) return NotFound();
            request.RequestDate = current.RequestDate;
            request.NeedByDate = current.NeedByDate;
        }
        else
        {
            request.RequestDate = DateTime.Today;
            request.NeedByDate = null;
        }

        if (!ModelState.IsValid)
            return View("Edit", await BuildEditViewModelAsync(request, null, ct));

        try
        {
            var id = await _service.SaveAsync(request, submitAfterSave, ct);
            TempData["Success"] = submitAfterSave
                ? "Đã tạo và gửi duyệt yêu cầu mua hàng."
                : "Đã lưu yêu cầu mua hàng. Bạn có thể tiếp tục chỉnh sửa trước khi gửi duyệt.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Edit", await BuildEditViewModelAsync(request, null, ct));
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var canViewStore = await HasPermissionAsync(PermissionCodes.Purchase.Request.ViewStore);
        var canViewOwn = await HasPermissionAsync(PermissionCodes.Purchase.Request.ViewOwn);
        if (!canViewStore && !canViewOwn) return Forbid();

        var canViewPurchaseOrders = await HasPermissionAsync(PermissionCodes.Purchase.Order.View) ||
                                    await HasPermissionAsync(PermissionCodes.Purchase.Order.Create) ||
                                    await HasPermissionAsync(PermissionCodes.Purchase.Order.Update) ||
                                    await HasPermissionAsync(PermissionCodes.Purchase.Order.Approve);
        var hasRequestViewCostPermission = await HasPermissionAsync(PermissionCodes.Purchase.Request.ViewCost);
        var hasOrderViewCostPermission = await HasPermissionAsync(PermissionCodes.Purchase.Order.ViewCost);
        var canViewCost = canViewPurchaseOrders &&
                          hasRequestViewCostPermission &&
                          hasOrderViewCostPermission;
        var detail = await _service.GetDetailAsync(id, requireOwnership: !canViewStore, includeCost: canViewCost, ct);
        if (detail == null) return NotFound();

        var currentUserId = GetCurrentUserId();
        var isOwner = currentUserId.HasValue && currentUserId.Value == detail.RequestedByUserId;
        var editableStatus = detail.Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.ReturnedForRevision;

        return View(new PurchaseRequestDetailsViewModel
        {
            Request = detail,
            IsOwner = isOwner,
            CanEdit = isOwner && editableStatus && await HasPermissionAsync(PermissionCodes.Purchase.Request.UpdateOwn),
            CanSubmit = isOwner && editableStatus && await HasPermissionAsync(PermissionCodes.Purchase.Request.Submit),
            CanReview = detail.Status == PurchaseRequestStatus.PendingApproval &&
                        await HasPermissionAsync(PermissionCodes.Purchase.Request.Review),
            CanConvert = detail.Status == PurchaseRequestStatus.Approved &&
                          detail.PurchaseOrders.Count == 0 &&
                          await HasPermissionAsync(PermissionCodes.Purchase.Request.Convert) &&
                          await HasPermissionAsync(PermissionCodes.Purchase.Order.Create),
            CanCancel = isOwner &&
                detail.Status is (PurchaseRequestStatus.Draft or PurchaseRequestStatus.ReturnedForRevision or PurchaseRequestStatus.PendingApproval) &&
                await HasPermissionAsync(PermissionCodes.Purchase.Request.UpdateOwn),
            CanViewCost = canViewCost,
            CanViewPurchaseOrders = canViewPurchaseOrders
        });
    }

    [HttpPost("{id:int}/submit")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.Submit)]
    public Task<IActionResult> Submit(int id, [FromForm] PurchaseRequestWorkflowRequest request, CancellationToken ct)
        => RunWorkflowAsync(() => _service.SubmitAsync(id, request, ct), id, "Đã gửi yêu cầu mua hàng để quản lý duyệt.");

    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.Review)]
    public Task<IActionResult> Approve(int id, [FromForm] ApprovePurchaseRequestRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Số lượng duyệt không hợp lệ. Vui lòng kiểm tra lại từng dòng.";
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Details), new { id }));
        }

        return RunWorkflowAsync(() => _service.ApproveAsync(id, request, ct), id, "Đã duyệt yêu cầu mua hàng.");
    }

    [HttpPost("{id:int}/return")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.Review)]
    public Task<IActionResult> Return(int id, [FromForm] PurchaseRequestWorkflowRequest request, CancellationToken ct)
        => RunWorkflowAsync(() => _service.ReturnForRevisionAsync(id, request, ct), id, "Đã trả yêu cầu cho nhân viên chỉnh sửa.");

    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.Review)]
    public Task<IActionResult> Reject(int id, [FromForm] PurchaseRequestWorkflowRequest request, CancellationToken ct)
        => RunWorkflowAsync(() => _service.RejectAsync(id, request, ct), id, "Đã từ chối yêu cầu mua hàng.");

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = PermissionCodes.Purchase.Request.UpdateOwn)]
    public Task<IActionResult> Cancel(int id, [FromForm] PurchaseRequestWorkflowRequest request, CancellationToken ct)
        => RunWorkflowAsync(
            () => _service.CancelAsync(id, request, requireOwnership: true, ct),
            id,
            "Đã hủy yêu cầu mua hàng.");

    private async Task<PurchaseRequestEditViewModel> BuildEditViewModelAsync(
        SavePurchaseRequestRequest request,
        PurchaseRequestDetailDto? detail,
        CancellationToken ct)
    {
        detail ??= request.Id.HasValue
            ? await _service.GetDetailAsync(request.Id.Value, requireOwnership: true, ct)
            : null;

        return new PurchaseRequestEditViewModel
        {
            Request = request,
            RequestNumber = detail?.RequestNumber,
            WorkflowNote = detail?.WorkflowNote,
            Status = detail?.Status ?? PurchaseRequestStatus.Draft,
            SelectedProducts = await _service.GetSelectedProductsAsync(
                request.Lines.Where(x => x.ProductUnitConversionId.HasValue && x.ProductUnitConversionId.Value > 0)
                    .Select(x => x.ProductUnitConversionId!.Value)
                    .Distinct()
                    .ToArray(),
                ct),
            CanSubmit = await HasPermissionAsync(PermissionCodes.Purchase.Request.Submit),
            UnitNames = await GetUnitNamesAsync(ct)
        };
    }

    private async Task<IActionResult> RenderPrepareAsync(
        int id,
        ConvertPurchaseRequestRequest request,
        string? supplierLabel,
        CancellationToken ct)
    {
        try
        {
            var preparation = await _service.GetPreparationAsync(id, includeCost: false, ct);
            return View("Prepare", BuildPrepareViewModel(preparation, request, supplierLabel));
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    private static PurchaseRequestPrepareViewModel BuildPrepareViewModel(
        PurchaseRequestPreparationDto preparation,
        ConvertPurchaseRequestRequest request,
        string? supplierLabel)
    {
        var defaults = BuildDefaultConversionRequest(preparation);
        var defaultOrder = defaults.Orders[0];
        var order = request.Orders.FirstOrDefault() ?? defaultOrder;
        order.Lines ??= new List<PurchaseRequestConversionLineRequest>();

        // The one-request/one-order workflow never accepts a subset or a partial
        // quantity. Rebuild identity and quantity from the approved request
        // returned by the server. Purchase orders intentionally do not capture
        // price/VAT; actual commercial values are entered when goods are received.
        order.Lines = defaultOrder.Lines.Select(line =>
        {
            return new PurchaseRequestConversionLineRequest
            {
                PurchaseRequestLineId = line.PurchaseRequestLineId,
                Quantity = line.Quantity,
                UnitPriceBeforeVat = 0m,
                TaxId = 0
            };
        }).ToList();
        order.HasVat = false;
        if (string.IsNullOrWhiteSpace(order.ClientGroupKey) || order.ClientGroupKey.Length > 64)
            order.ClientGroupKey = defaultOrder.ClientGroupKey;
        request.Orders = new List<PurchaseRequestOrderGroupRequest> { order };
        request.RowVersion ??= preparation.RowVersion;

        var label = supplierLabel?.Trim();
        if (string.IsNullOrWhiteSpace(label) && order.SupplierId > 0)
        {
            label = preparation.Lines
                .FirstOrDefault(x => x.SuggestedSupplierId == order.SupplierId)
                ?.SuggestedSupplierName;
        }

        return new PurchaseRequestPrepareViewModel
        {
            Preparation = preparation,
            Request = request,
            SupplierLabel = label ?? string.Empty
        };
    }

    private static ConvertPurchaseRequestRequest BuildDefaultConversionRequest(
        PurchaseRequestPreparationDto preparation)
    {
        var legalEntityId = preparation.DefaultLegalEntityId
            ?? preparation.LegalEntities.Select(x => (int?)x.Id).FirstOrDefault()
            ?? 0;
        var warehouseId = preparation.Warehouses
            .Where(x => x.LegalEntityId == legalEntityId)
            .Select(x => x.Id)
            .FirstOrDefault();

        var remainingLines = preparation.Lines
            .Where(x => x.ApprovedQuantity > 0)
            .ToList();
        var suggestedSuppliers = remainingLines
            .Where(x => x.SuggestedSupplierId.HasValue && !string.IsNullOrWhiteSpace(x.SuggestedSupplierName))
            .Select(x => new { x.SuggestedSupplierId, x.SuggestedSupplierName })
            .Distinct()
            .ToList();
        var suggestedSupplier = suggestedSuppliers.Count == 1
            ? suggestedSuppliers[0]
            : null;
        var title = preparation.Title.Length <= 250
            ? preparation.Title
            : preparation.Title[..250];
        var order = new PurchaseRequestOrderGroupRequest
        {
            // Unique per rendered form and posted back unchanged for idempotent retry.
            ClientGroupKey = $"pr-{preparation.PurchaseRequestId}-{Guid.NewGuid():N}",
            Title = title,
            SupplierId = suggestedSupplier?.SuggestedSupplierId ?? 0,
            LegalEntityId = legalEntityId,
            ExpectedWarehouseId = warehouseId,
            OrderDate = DateTime.Today,
            HasVat = false,
            Note = $"Tạo từ yêu cầu mua {preparation.RequestNumber}.",
            Lines = remainingLines.Select(line => new PurchaseRequestConversionLineRequest
            {
                PurchaseRequestLineId = line.PurchaseRequestLineId,
                Quantity = line.ApprovedQuantity,
                UnitPriceBeforeVat = 0m,
                TaxId = 0
            }).ToList()
        };

        return new ConvertPurchaseRequestRequest
        {
            RowVersion = preparation.RowVersion,
            Orders = new List<PurchaseRequestOrderGroupRequest> { order }
        };
    }

    private async Task<IActionResult> RunWorkflowAsync(Func<Task> action, int id, string successMessage)
    {
        try
        {
            await action();
            TempData["Success"] = successMessage;
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<bool> CanUseEditorAsync()
        => await HasPermissionAsync(PermissionCodes.Purchase.Request.Create) ||
           await HasPermissionAsync(PermissionCodes.Purchase.Request.UpdateOwn);

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

    private async Task<bool> HasPermissionAsync(string permission)
        => (await _authorization.AuthorizeAsync(User, null, permission)).Succeeded;

    private int? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("UserId");
        return int.TryParse(value, out var userId) ? userId : null;
    }
}
