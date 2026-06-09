using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/stock-counts")]
[Authorize]
[ApiController]
public class StockCountsController : ControllerBase
{
    private readonly IStockCountService _stockCountService;

    public StockCountsController(IStockCountService stockCountService)
    {
        _stockCountService = stockCountService;
    }
    [Authorize(Policy = PermissionCodes.Inventory.StockCount.View)]
    [HttpGet]
    public async Task<IActionResult> GetList(CancellationToken ct)
    {
        var result = await _stockCountService.GetListAsync(ct);
        return Ok(result);
    }
    [Authorize(Policy = PermissionCodes.Inventory.StockCount.Create)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStockCountDocumentRequest request, CancellationToken ct)
    {
        var id = await _stockCountService.CreateAsync(request, ct);
        return Ok(new
        {
            id,
            message = "Tạo phiếu kiểm kê thành công."
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id, CancellationToken ct)
    {
        var result = await _stockCountService.GetDetailAsync(id, ct);
        if (result == null)
            return NotFound(new { message = "Không tìm thấy phiếu kiểm kê." });

        return Ok(result);
    }
    [Authorize(Policy = PermissionCodes.Inventory.StockCount.Update)]
    [HttpPost("update-header")]
    public async Task<IActionResult> UpdateHeader([FromBody] UpdateStockCountDocumentHeaderRequest request, CancellationToken ct)
    {
        await _stockCountService.UpdateHeaderAsync(request, ct);
        return Ok(new { message = "Cập nhật header phiếu kiểm kê thành công." });
    }
    [Authorize(Policy = PermissionCodes.Inventory.StockCount.Update)]

    [HttpPost("{stockCountDocumentId:int}/lines")]
    public async Task<IActionResult> AddLine(int stockCountDocumentId, [FromBody] AddStockCountLineRequest request, CancellationToken ct)
    {
        var lineId = await _stockCountService.AddLineAsync(stockCountDocumentId, request, ct);
        return Ok(new
        {
            id = lineId,
            message = "Thêm dòng kiểm kê thành công."
        });
    }

    [HttpPut("lines/{lineId:int}")]
    public async Task<IActionResult> UpdateLine(int lineId, [FromBody] UpdateStockCountLineRequest request, CancellationToken ct)
    {
        await _stockCountService.UpdateLineAsync(lineId, request, ct);
        return Ok(new { message = "Cập nhật dòng kiểm kê thành công." });
    }

    [HttpDelete("lines/{lineId:int}")]
    public async Task<IActionResult> DeleteLine(int lineId, CancellationToken ct)
    {
        await _stockCountService.DeleteLineAsync(lineId, ct);
        return Ok(new { message = "Xóa dòng kiểm kê thành công." });
    }
    [Authorize(Policy = PermissionCodes.Inventory.StockCount.Approve)]
    [HttpPost("{stockCountDocumentId:int}/confirm")]
    public async Task<IActionResult> Confirm(int stockCountDocumentId, CancellationToken ct)
    {
        await _stockCountService.ConfirmAsync(stockCountDocumentId, ct);
        return Ok(new
        {
            message = "Xác nhận phiếu kiểm kê thành công."
        });
    }

    [HttpPost("{stockCountDocumentId:int}/submit-approval")]
    public async Task<IActionResult> SubmitForApproval(int stockCountDocumentId, CancellationToken ct)
    {
        await _stockCountService.SubmitForApprovalAsync(stockCountDocumentId, ct);
        return Ok(new { message = "Đã gửi phiếu kiểm kê chờ duyệt." });
    }

    [HttpPost("{stockCountDocumentId:int}/reject")]
    public async Task<IActionResult> Reject(int stockCountDocumentId, CancellationToken ct)
    {
        await _stockCountService.RejectAsync(stockCountDocumentId, ct);
        return Ok(new { message = "Đã từ chối phiếu kiểm kê." });
    }
    [HttpPost("{stockCountDocumentId:int}/refresh-system-qty")]
    public async Task<IActionResult> RefreshSystemQty(int stockCountDocumentId, CancellationToken ct)
    {
        await _stockCountService.RefreshSystemQtyAsync(stockCountDocumentId, ct);
        return Ok(new { message = "Đã làm mới tồn hệ thống cho phiếu kiểm kê." });
    }
}