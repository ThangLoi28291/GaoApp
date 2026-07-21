using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

/// <summary>
/// API controller xử lý nghiệp vụ chuyển kho.
/// 
/// Quy ước:
/// - UI chỉ gọi ajax tới đây
/// - Service throw InvalidOperationException
/// - Controller convert sang JSON lỗi thân thiện
/// </summary>
[Area("Admin")]
[Authorize]
[Route("admin/api/stock-transfers")]
[ApiController]
[AutoValidateAntiforgeryToken]
public class StockTransfersController : ControllerBase
{
    private readonly IStockTransferService _stockTransferService;

    public StockTransfersController(
        IStockTransferService stockTransferService)
    {
        _stockTransferService = stockTransferService;
    }

    /// <summary>
    /// Lấy danh sách phiếu chuyển kho.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.View)]
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
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : pageSize;

        // Chặn pageSize quá lớn gây lag
        if (pageSize > 200)
            pageSize = 200;

        var result = await _stockTransferService.GetListAsync(
            page,
            pageSize,
            keyword,
            fromWarehouseId,
            toWarehouseId,
            status,
            fromDate,
            toDate,
            ct);

        return Ok(result);
    }

    /// <summary>
    /// Tạo phiếu chuyển kho.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.Create)]
    public async Task<IActionResult> Create(
        [FromBody] CreateStockTransferDocumentRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ToValidationError());

        try
        {
            var id = await _stockTransferService.CreateAsync(request, ct);

            return Ok(new
            {
                id,
                message = "Tạo phiếu chuyển kho thành công."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Lấy chi tiết phiếu chuyển kho.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.View)]
    public async Task<IActionResult> GetDetail(
        int id,
        CancellationToken ct)
    {
        var result = await _stockTransferService.GetDetailAsync(id, ct);

        if (result == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy phiếu chuyển kho."
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Cập nhật thông tin header phiếu chuyển kho.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.Update)]
    public async Task<IActionResult> UpdateHeader(
        int id,
        [FromBody] UpdateStockTransferHeaderRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ToValidationError());

        try
        {
            await _stockTransferService.UpdateHeaderAsync(id, request, ct);

            return Ok(new
            {
                message = "Đã lưu thông tin phiếu chuyển kho."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Thêm dòng chuyển kho.
    /// </summary>
    [HttpPost("{id:int}/lines")]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.Update)]
    public async Task<IActionResult> AddLine(
        int id,
        [FromBody] AddStockTransferLineRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ToValidationError());

        try
        {
            var lineId = await _stockTransferService.AddLineAsync(id, request, ct);

            return Ok(new
            {
                id = lineId,
                message = "Đã thêm sản phẩm vào phiếu chuyển kho."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Cập nhật dòng chuyển kho.
    /// </summary>
    [HttpPut("lines/{lineId:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.Update)]
    public async Task<IActionResult> UpdateLine(
        int lineId,
        [FromBody] UpdateStockTransferLineRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ToValidationError());

        try
        {
            await _stockTransferService.UpdateLineAsync(lineId, request, ct);

            return Ok(new
            {
                message = "Cập nhật dòng chuyển kho thành công."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Xóa dòng chuyển kho.
    /// </summary>
    [HttpDelete("lines/{lineId:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.Delete)]
    public async Task<IActionResult> DeleteLine(
        int lineId,
        CancellationToken ct)
    {
        try
        {
            await _stockTransferService.DeleteLineAsync(lineId, ct);

            return Ok(new
            {
                message = "Đã xóa dòng chuyển kho."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Submit phiếu chuyển kho sang trạng thái chờ duyệt.
    /// </summary>
    [HttpPost("{id:int}/submit")]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.Update)]
    public async Task<IActionResult> Submit(
        int id,
        CancellationToken ct)
    {
        try
        {
            await _stockTransferService.SubmitAsync(id, ct);

            return Ok(new
            {
                message = "Đã gửi phiếu chuyển kho sang trạng thái chờ duyệt."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Reject phiếu chuyển kho.
    /// </summary>
    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.Approve)]
    public async Task<IActionResult> Reject(
        int id,
        [FromBody] RejectStockTransferRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ToValidationError());

        try
        {
            await _stockTransferService.RejectAsync(
                id,
                request?.Reason,
                ct);

            return Ok(new
            {
                message = "Đã từ chối phiếu chuyển kho."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Confirm phiếu chuyển kho.
    /// Khi confirm sẽ sinh movement tồn kho.
    /// </summary>
    [HttpPost("{id:int}/confirm")]
    [Authorize(Policy = PermissionCodes.Inventory.StockTransfer.Confirm)]
    public async Task<IActionResult> Confirm(
        int id,
        CancellationToken ct)
    {
        try
        {
            await _stockTransferService.ConfirmAsync(id, ct);

            return Ok(new
            {
                message = "Đã xác nhận phiếu chuyển kho."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Convert validation error sang JSON dễ hiển thị toast.
    /// </summary>
    private object ToValidationError()
    {
        var errors = ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .SelectMany(x => x.Value!.Errors)
            .Select(x => x.ErrorMessage)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return new
        {
            message = errors.Count > 0
                ? string.Join(" ", errors)
                : "Dữ liệu không hợp lệ."
        };
    }
}
