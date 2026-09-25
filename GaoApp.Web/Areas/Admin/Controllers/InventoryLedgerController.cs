using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/inventory-ledger")]
[Authorize]
[Authorize(Policy = PermissionCodes.Inventory.Transaction.View)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InventoryLedgerController : Controller
{
    private readonly IInventoryLedgerIndexReadService _readService;

    public InventoryLedgerController(IInventoryLedgerIndexReadService readService)
    {
        _readService = readService;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["Title"] = "Thẻ kho / Lịch sử giao dịch kho";
        return View();
    }

    [HttpGet("data")]
    public async Task<IActionResult> GetData(
        [FromQuery] InventoryLedgerIndexQueryRequest request,
        CancellationToken ct)
    {
        var result = await _readService.GetPageAsync(request, ct);
        return Ok(result);
    }

    [HttpGet("quick-view")]
    public async Task<IActionResult> GetQuickView(
        [FromQuery] int transactionId,
        CancellationToken ct)
    {
        if (transactionId <= 0)
            return BadRequest(new { message = "Giao dịch cần xem không hợp lệ." });

        var result = await _readService.GetQuickViewAsync(transactionId, ct);

        return result is null
            ? NotFound(new { message = "Không tìm thấy giao dịch kho." })
            : Ok(result);
    }
}
