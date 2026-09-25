using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Application.Interfaces.Services.Purchases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/purchase-receiving")]
[Authorize]
[AutoValidateAntiforgeryToken]
public sealed class PurchaseReceivingController : Controller
{
    private readonly IPurchaseReceivingWorkbenchService _service;
    private readonly IBarcodeLookupService _barcodes;
    private readonly IConfiguration _configuration;
    private readonly IAuthorizationService _authorizationService;

    public PurchaseReceivingController(
        IPurchaseReceivingWorkbenchService service,
        IBarcodeLookupService barcodes,
        IConfiguration configuration,
        IAuthorizationService authorizationService)
    {
        _service = service;
        _barcodes = barcodes;
        _configuration = configuration;
        _authorizationService = authorizationService;
    }

    [HttpPost("purchase-orders/{purchaseOrderId:int}/start")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Create)]
    public async Task<IActionResult> Start(int purchaseOrderId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        try
        {
            var model = await _service.StartOrResumeAsync(purchaseOrderId, ct);
            TempData["ReceivingLeaseToken"] = model.LeaseToken?.ToString();
            return RedirectToAction(nameof(Index), new { id = model.StockDocumentId });
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.SafeMessage;
            return RedirectToAction("Details", "PurchaseOrders", new { id = purchaseOrderId });
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Index(int id, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!await HasAnyPermissionAsync(
                PermissionCodes.Purchase.Receipt.View,
                PermissionCodes.Purchase.Receipt.Update,
                PermissionCodes.Purchase.Receipt.Approve)) return Forbid();
        var model = await _service.GetAsync(id, ct);
        ViewData["InitialLeaseToken"] = TempData["ReceivingLeaseToken"] as string;
        ViewData["CanApproveOutsidePo"] =
            (await _authorizationService.AuthorizeAsync(
                User, PermissionCodes.Purchase.Receipt.Approve)).Succeeded;
        ViewData["ProvisionalItemsEnabled"] = _configuration.GetValue<bool>(
            "ReceivingWorkbench:ProvisionalItemsEnabled");
        return View(model);
    }

    [HttpGet("lookup")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public async Task<IActionResult> Lookup([FromQuery] string? term, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        var items = await _barcodes.SearchForStockDocumentSelect2Async(term ?? string.Empty, 20, ct);
        return Json(new
        {
            results = items.Where(x => x.ProductUnitConversionId.HasValue).Select(x => new
            {
                id = x.ProductUnitConversionId,
                text = x.Text,
                x.ProductVariantId,
                x.UnitName,
                x.BaseUnitName,
                x.IsBaseUnit,
                x.Factor,
                x.Barcode
            })
        });
    }

    [HttpPost("{id:int}/acquire")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Acquire(int id, [FromBody] PurchaseReceivingLeaseRequest request, CancellationToken ct)
        => JsonCall(() => _service.AcquireAsync(id, request, ct));

    [HttpPost("{id:int}/heartbeat")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Heartbeat(int id, [FromBody] PurchaseReceivingLeaseRequest request, CancellationToken ct)
        => JsonCall(() => _service.HeartbeatAsync(id, request, ct));

    [HttpPost("{id:int}/scan")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Scan(int id, [FromBody] PurchaseReceivingScanRequest request, CancellationToken ct)
        => JsonCall(() => _service.ScanAsync(id, request, ct));

    [HttpPost("{id:int}/add")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Add(int id, [FromBody] PurchaseReceivingAddRequest request, CancellationToken ct)
        => JsonCall(() => _service.AddAsync(id, request, bulk: false, ct));

    [HttpPost("{id:int}/bulk")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Bulk(int id, [FromBody] PurchaseReceivingAddRequest request, CancellationToken ct)
        => JsonCall(() => _service.AddAsync(id, request, bulk: true, ct));

    [HttpPost("{id:int}/lines/{lineId:int}/edit")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Edit(int id, int lineId, [FromBody] PurchaseReceivingEditRequest request, CancellationToken ct)
        => JsonCall(() => _service.EditAsync(id, lineId, request, ct));

    [HttpPost("{id:int}/lines/{lineId:int}/remove")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Remove(int id, int lineId, [FromBody] PurchaseReceivingCommandRequest request, CancellationToken ct)
        => JsonCall(() => _service.RemoveAsync(id, lineId, request, ct));

    [HttpPost("{id:int}/undo")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Undo(int id, [FromBody] PurchaseReceivingCommandRequest request, CancellationToken ct)
        => JsonCall(() => _service.UndoAsync(id, request, ct));

    [HttpPost("{id:int}/finish")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Update)]
    public Task<IActionResult> Finish(int id, [FromBody] PurchaseReceivingLeaseRequest request, CancellationToken ct)
        => JsonCall(() => _service.FinishAsync(id, request, ct));

    [HttpPost("{id:int}/outside/{lineId:int}")]
    [Authorize(Policy = PermissionCodes.Purchase.Receipt.Approve)]
    public Task<IActionResult> Outside(int id, int lineId, [FromBody] PurchaseReceivingOutsideDecisionRequest request, CancellationToken ct)
        => JsonCall(() => _service.DecideOutsideAsync(id, lineId, request, ct));

    private async Task<IActionResult> JsonCall(Func<Task<PurchaseReceivingWorkbenchDto>> action)
    {
        if (!Enabled) return NotFound();
        try { return Json(await action()); }
        catch (BusinessRuleException ex) { return Conflict(new { message = ex.SafeMessage }); }
    }

    private bool Enabled => _configuration.GetValue<bool>("ReceivingWorkbench:Enabled");

    private async Task<bool> HasAnyPermissionAsync(params string[] policies)
    {
        foreach (var policy in policies)
            if ((await _authorizationService.AuthorizeAsync(User, policy)).Succeeded) return true;
        return false;
    }
}
