using System.ComponentModel.DataAnnotations;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Policy = PermissionCodes.Pos.Shift.View), AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("admin/pos-shift/cash-adjustments")]
public sealed class POSCashAdjustmentsController(IPOSCashAdjustmentService service) : Controller
{
    [HttpGet("")]
    public IActionResult Index() => LocalRedirect(POSRequestNavigation.FromLegacy(Request.Query, "cash"));

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions([Range(1, 100000000)] int page = 1,
        [StringLength(200)] string? keyword = null, int? transactionId = null, int? shiftId = null, CancellationToken ct = default)
        => !ModelState.IsValid ? BadRequest(new { message = "Bộ lọc không hợp lệ." }) : Ok(await service.TransactionsAsync(page, keyword, transactionId, shiftId, ct));

    [HttpGet("data")]
    public async Task<IActionResult> List([Range(1, 100000000)] int page = 1, string? status = null, int? shiftId = null, CancellationToken ct = default)
        => !ModelState.IsValid ? BadRequest(new { message = "Bộ lọc không hợp lệ." }) : Ok(await service.ListAsync(page, status, shiftId, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct) => Ok(await service.DetailAsync(id, ct));

    [HttpPost("transactions/{id:int}/requests"), Authorize(Policy = PermissionCodes.Pos.Shift.Reconcile)]
    public async Task<IActionResult> Create(int id, [FromBody] CreateCashAdjustmentRequest request, CancellationToken ct)
        => !ModelState.IsValid ? BadRequest(new { message = "Kiểm tra loại thu/chi, số tiền, nội dung và lý do đề nghị." })
            : Ok(new { id = await service.CreateAsync(id, request, ct) });

    [HttpPost("{id:int}/{actionName}")]
    public async Task<IActionResult> Decide(int id, string actionName, [FromBody] CashAdjustmentDecision request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Thiếu phiên bản dữ liệu hoặc ghi chú vượt 500 ký tự." });
        await service.DecideAsync(id, actionName, request, ct);
        return Ok(new { success = true });
    }

    [HttpPost("shifts/{id:int}/reconcile")]
    public async Task<IActionResult> Reconcile(int id, [FromBody] CashAdjustmentDecision request, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "Kiểm tra phiên bản dữ liệu và ghi chú đối soát." });
        await service.ReconcileAsync(id, request, ct);
        return Ok(new { success = true });
    }
}
