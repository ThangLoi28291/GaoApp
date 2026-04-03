using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/stock-transfers")]
[ApiController]
public class StockTransfersController : ControllerBase
{
    private readonly IStockTransferService _stockTransferService;

    public StockTransfersController(IStockTransferService stockTransferService)
    {
        _stockTransferService = stockTransferService;
    }

    /// <summary>
    /// Lấy danh sách phiếu chuyển kho.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] int? fromWarehouseId = null,
        [FromQuery] int? toWarehouseId = null,
        [FromQuery] int? status = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        CancellationToken ct = default)
    {
        var result = await _stockTransferService.GetListAsync(
            page, pageSize, keyword, fromWarehouseId, toWarehouseId, status, fromDate, toDate, ct);

        return Ok(result);
    }

    /// <summary>
    /// Tạo phiếu chuyển kho.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStockTransferDocumentRequest request, CancellationToken ct)
    {
        var id = await _stockTransferService.CreateAsync(request, ct);
        return Ok(new { id, message = "Tạo phiếu chuyển kho thành công." });
    }

    /// <summary>
    /// Lấy chi tiết phiếu chuyển kho.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id, CancellationToken ct)
    {
        var result = await _stockTransferService.GetDetailAsync(id, ct);
        if (result == null)
            return NotFound(new { message = "Không tìm thấy phiếu chuyển kho." });

        return Ok(result);
    }

    /// <summary>
    /// Cập nhật header phiếu chuyển kho.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateHeader(int id, [FromBody] UpdateStockTransferHeaderRequest request, CancellationToken ct)
    {
        await _stockTransferService.UpdateHeaderAsync(id, request, ct);
        return Ok(new { message = "Cập nhật header phiếu chuyển kho thành công." });
    }

    /// <summary>
    /// Thêm dòng chuyển kho.
    /// </summary>
    [HttpPost("{id:int}/lines")]
    public async Task<IActionResult> AddLine(int id, [FromBody] AddStockTransferLineRequest request, CancellationToken ct)
    {
        var lineId = await _stockTransferService.AddLineAsync(id, request, ct);
        return Ok(new { id = lineId, message = "Thêm dòng chuyển kho thành công." });
    }

    /// <summary>
    /// Cập nhật dòng chuyển kho.
    /// </summary>
    [HttpPut("lines/{lineId:int}")]
    public async Task<IActionResult> UpdateLine(int lineId, [FromBody] UpdateStockTransferLineRequest request, CancellationToken ct)
    {
        await _stockTransferService.UpdateLineAsync(lineId, request, ct);
        return Ok(new { message = "Cập nhật dòng chuyển kho thành công." });
    }

    /// <summary>
    /// Xóa dòng chuyển kho.
    /// </summary>
    [HttpDelete("lines/{lineId:int}")]
    public async Task<IActionResult> DeleteLine(int lineId, CancellationToken ct)
    {
        await _stockTransferService.DeleteLineAsync(lineId, ct);
        return Ok(new { message = "Xóa dòng chuyển kho thành công." });
    }

    /// <summary>
    /// Submit phiếu chuyển kho.
    /// </summary>
    [HttpPost("{id:int}/submit")]
    public async Task<IActionResult> Submit(int id, CancellationToken ct)
    {
        await _stockTransferService.SubmitAsync(id, ct);
        return Ok(new { message = "Đã submit phiếu chuyển kho." });
    }

    /// <summary>
    /// Reject phiếu chuyển kho.
    /// </summary>
    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectStockTransferRequest request, CancellationToken ct)
    {
        await _stockTransferService.RejectAsync(id, request?.Reason, ct);
        return Ok(new { message = "Đã reject phiếu chuyển kho." });
    }

    /// <summary>
    /// Confirm phiếu chuyển kho và sinh ledger.
    /// </summary>
    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        await _stockTransferService.ConfirmAsync(id, ct);
        return Ok(new { message = "Đã confirm phiếu chuyển kho." });
    }
}
