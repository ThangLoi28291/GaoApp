using GaoApp.Application.DTOs.Returns;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/pos/returns")]
[Authorize(Policy = PermissionCodes.Pos.Order.View)]
public class POSReturnController : Controller
{
    private readonly ISalesReturnService _salesReturnService;
    private readonly IPendingReturnRestockService _pendingRestock;

    public POSReturnController(ISalesReturnService salesReturnService, IPendingReturnRestockService pendingRestock)
    {
        _salesReturnService = salesReturnService;
        _pendingRestock = pendingRestock;
    }

    [HttpGet("order/{orderId:int}/eligibility")]
    public async Task<IActionResult> GetEligibility(int orderId, CancellationToken ct)
    {
        var result = await _salesReturnService.GetEligibilityAsync(orderId, ct);
        return Ok(result);
    }

    [HttpGet("order/{orderId:int}/history")]
    public async Task<IActionResult> GetHistory(int orderId, CancellationToken ct)
    {
        var result = await _salesReturnService.GetByOrderIdAsync(orderId, ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = PermissionCodes.Pos.Order.Refund)]
    [Authorize(Policy = PermissionCodes.Pos.Payment.Refund)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateSalesReturnRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _salesReturnService.CreateAsync(request, ct);
            return Ok(new
            {
                success = true,
                message = "Đã tạo phiếu trả hàng / hoàn tiền thành công.",
                data = result
            });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.SafeMessage,
                errorCode = "POS_RETURN_INVALID",
                errorType = "business_rule"
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                success = false,
                message = ex.Message,
                errorCode = "POS_RETURN_STATE_CHANGED",
                errorType = "state_conflict",
                actionHint = "Dữ liệu đơn hoặc tồn kho đã thay đổi. Hãy đóng popup, tải lại chi tiết đơn rồi thực hiện lại."
            });
        }
    }

    [HttpGet("pending-restock")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    public async Task<IActionResult> PendingRestock(CancellationToken ct)
        => View(await _pendingRestock.GetPendingAsync(ct));

    [HttpPost("{returnId:int}/complete-restock")]
    [Authorize(Policy = PermissionCodes.Inventory.StockDocument.Approve)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteRestock(int returnId, CancellationToken ct)
    {
        await _pendingRestock.CompleteAsync(returnId, ct);
        return Ok(new {success = true, message = "Đã hoàn tất nhập kho hàng trả. Không phát sinh thêm tiền hoàn."});
    }
}
