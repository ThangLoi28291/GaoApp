using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[ApiController]
[Route("admin/pos/shift")]
public class POSShiftController : ControllerBase
{
    private readonly IPOSShiftService _service;

    public POSShiftController(IPOSShiftService service)
    {
        _service = service;
    }

    /// <summary>
    /// GET: /admin/pos/shift/current
    /// Lấy ca POS đang mở hiện tại
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var shift = await _service.GetCurrentOpenAsync(ct);
        return Ok(shift);
    }

    /// <summary>
    /// POST: /admin/pos/shift/open
    /// Mở ca mới
    /// </summary>
    [HttpPost("open")]
    public async Task<IActionResult> Open([FromBody] OpenShiftRequest req, CancellationToken ct)
    {
        var result = await _service.OpenAsync(req, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST: /admin/pos/shift/close
    /// Đóng ca hiện tại
    /// </summary>
    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] CloseShiftRequest req, CancellationToken ct)
    {
        var result = await _service.CloseAsync(req, ct);
        return Ok(result);
    }
    /// <summary>
    /// Tạo phiếu thu/chi trong ca hiện tại
    /// POST: /admin/pos/shift/cash-transaction
    /// </summary>
    [HttpPost("cash-transaction")]
    public async Task<IActionResult> AddCashTransaction([FromBody] CreatePosShiftCashTransactionRequest dto, CancellationToken ct)
    {
        var result = await _service.AddCashTransactionAsync(dto, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách phiếu thu/chi của ca đang mở
    /// GET: /admin/pos/shift/cash-transactions
    /// </summary>
    [HttpGet("cash-transactions")]
    public async Task<IActionResult> GetCashTransactions(CancellationToken ct)
    {
        var result = await _service.GetCashTransactionsAsync(ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int? shiftId, CancellationToken ct)
    {
        var result = await _service.GetSummaryAsync(shiftId, ct);
        return Ok(result);
    }
    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var result = await _service.GetCurrentOpenAsync(ct);
        return Ok(result);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] POSShiftStatus? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtcExclusive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var req = new QueryPOSShiftHistoryRequest
        {
            Status = status,
            FromUtc = fromUtc,
            ToUtcExclusive = toUtcExclusive,
            Page = page,
            PageSize = pageSize
        };

        var result = await _service.QueryHistoryAsync(req, ct);
        return Ok(result);
    }
    
}