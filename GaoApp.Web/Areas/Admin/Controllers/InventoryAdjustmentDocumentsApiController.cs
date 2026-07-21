using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[ApiController]
[Authorize]
[Route("admin/api/inventory-adjustment-documents")]
[AutoValidateAntiforgeryToken]
public class InventoryAdjustmentDocumentsApiController : ControllerBase
{
    private readonly IInventoryAdjustmentDocumentService _service;
    private readonly IInventoryCostSuggestionService _costSuggestionService;
    private readonly IAuthorizationService _authorizationService;

    public InventoryAdjustmentDocumentsApiController(
     IInventoryAdjustmentDocumentService service,
     IInventoryCostSuggestionService costSuggestionService,
     IAuthorizationService authorizationService)
    {
        _service = service;
        _costSuggestionService = costSuggestionService;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Danh sách phiếu điều chỉnh kho.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.View)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] InventoryAdjustmentDocumentFilterDto filter,
        CancellationToken ct)
    {
        var result = await _service.GetPagedAsync(filter, ct);
        return Ok(result);
    }

    /// <summary>
    /// Chi tiết phiếu điều chỉnh kho.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.View)]
    public async Task<IActionResult> GetDetail(
        int id,
        CancellationToken ct)
    {
        var result = await _service.GetDetailAsync(id, ct);

        if (result == null)
            return NotFound(new { message = "Phiếu điều chỉnh kho không tồn tại." });

        return Ok(result);
    }

    /// <summary>
    /// Tạo phiếu điều chỉnh kho.
    /// Chỉ tạo Draft, chưa tác động tồn kho.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Create)]
    public async Task<IActionResult> Create(
        [FromBody] CreateInventoryAdjustmentDocumentRequest request,
        CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Sửa phiếu điều chỉnh kho.
    /// Chỉ cho sửa Draft.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Update)]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateInventoryAdjustmentDocumentRequest request,
        CancellationToken ct)
    {
        request.Id = id;

        var result = await _service.UpdateAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Gửi duyệt phiếu điều chỉnh kho.
    /// </summary>
    [HttpPost("{id:int}/submit")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Submit)]
    public async Task<IActionResult> Submit(
        int id,
        CancellationToken ct)
    {
        var result = await _service.SubmitAsync(
            new SubmitInventoryAdjustmentDocumentRequest
            {
                Id = id
            },
            ct);

        return Ok(result);
    }

    /// <summary>
    /// Xóa phiếu Draft.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Delete)]
    public async Task<IActionResult> DeleteDraft(
        int id,
        CancellationToken ct)
    {
        await _service.DeleteDraftAsync(id, ct);
        return Ok(new { message = "Đã xóa phiếu điều chỉnh kho." });
    }
    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Approve)]
    public async Task<IActionResult> Approve(
    int id,
    [FromBody] ApproveInventoryAdjustmentDocumentRequest request,
    CancellationToken ct)
    {
        request.Id = id;
        var result = await _service.ApproveAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Reject)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] RejectInventoryAdjustmentDocumentRequest request,
        CancellationToken ct)
    {
        request.Id = id;
        var result = await _service.RejectAsync(request, ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Cancel)]
    public async Task<IActionResult> Cancel(
        int id,
        [FromBody] CancelInventoryAdjustmentDocumentRequest request,
        CancellationToken ct)
    {
        request.Id = id;
        var result = await _service.CancelAsync(request, ct);
        return Ok(result);
    }
    [HttpGet("{id:int}/cost-suggestions")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Approve)]
    public async Task<IActionResult> GetCostSuggestions(
    int id,
    CancellationToken ct)
    {
        var doc = await _service.GetDetailAsync(id, ct);
        if (doc == null)
            return NotFound(new { message = "Phiếu không tồn tại." });

        var results = new List<object>();

        foreach (var line in doc.Lines)
        {
            var suggestion = await _costSuggestionService.GetSuggestedCostAsync(
                line.ProductVariantId,
                ct);

            results.Add(new
            {
                lineId = line.Id,
                productVariantId = line.ProductVariantId,
                productName = line.ProductName,
                unitCost = line.UnitCost ?? suggestion.UnitCost,
                sourceText = line.UnitCost.HasValue
                    ? "Giá vốn nhập trên phiếu"
                    : suggestion.SourceText,
                hasValidCost = line.UnitCost.HasValue && line.UnitCost.Value > 0 || suggestion.HasValidCost
            });
        }

        return Ok(results);
    }
    [HttpGet("permissions")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.View)]
    public async Task<IActionResult> GetPermissions()
    {
        async Task<bool> CanAsync(string permissionCode)
        {
            var result = await _authorizationService.AuthorizeAsync(
                User,
                null,
                permissionCode);

            return result.Succeeded;
        }

        return Ok(new
        {
            canView = await CanAsync(PermissionCodes.Inventory.Adjustment.View),
            canCreate = await CanAsync(PermissionCodes.Inventory.Adjustment.Create),
            canUpdate = await CanAsync(PermissionCodes.Inventory.Adjustment.Update),
            canDelete = await CanAsync(PermissionCodes.Inventory.Adjustment.Delete),
            canSubmit = await CanAsync(PermissionCodes.Inventory.Adjustment.Submit),
            canApprove = await CanAsync(PermissionCodes.Inventory.Adjustment.Approve),
            canReject = await CanAsync(PermissionCodes.Inventory.Adjustment.Reject),
            canCancel = await CanAsync(PermissionCodes.Inventory.Adjustment.Cancel)
        });
    }

}
