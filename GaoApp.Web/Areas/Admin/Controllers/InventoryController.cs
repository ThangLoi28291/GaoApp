using GaoApp.Web.Security;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Route("admin/api/inventory")]
[Authorize]
[ApiController]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IInventoryMovementService _inventoryMovementService;

    public InventoryController(
        IInventoryService inventoryService,
        IInventoryMovementService inventoryMovementService)
    {
        _inventoryService = inventoryService;
        _inventoryMovementService = inventoryMovementService;
    }

    [HttpPost("transactions")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.Approve)]
    public IActionResult CreateTransaction()
    {
        // This old generic API could invent sale/receipt/adjustment references.
        // All supported UI flows post through their own validated documents.
        return StatusCode(StatusCodes.Status410Gone, new
        {
            code = "INVENTORY_DOCUMENT_REQUIRED",
            message = "API ghi biến động tồn trực tiếp đã đóng. Hãy dùng chứng từ kho hoặc luồng POS tương ứng."
        });
    }
    [HttpGet("balances/by-variant/{productVariantId:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.Balance.View)]
    public async Task<IActionResult> GetBalancesByVariant(int productVariantId, CancellationToken ct)
    {
        var result = await _inventoryService.GetBalancesByVariantAsync(productVariantId, ct);
        return Ok(result);
    }

    [HttpGet("transactions/by-variant/{productVariantId:int}")]
    [Authorize(Policy = PermissionCodes.Inventory.Transaction.View)]
    public async Task<IActionResult> GetTransactionsByVariant(int productVariantId, CancellationToken ct)
    {
        var result = await _inventoryService.GetTransactionsByVariantAsync(productVariantId, ct);
        return Ok(result);
    }

    [HttpGet("negative-balances")]
    [Authorize(Policy = PermissionCodes.Inventory.Balance.View)]
    public async Task<IActionResult> GetNegativeBalances(CancellationToken ct)
    {
        var result = await _inventoryService.GetNegativeBalancesAsync(ct);
        return Ok(result);
    }

    [HttpGet("negative-logs")]
    [Authorize(Policy = PermissionCodes.Inventory.Transaction.View)]
    public async Task<IActionResult> GetNegativeLogs(CancellationToken ct)
    {
        var result = await _inventoryService.GetNegativeLogsAsync(ct);
        return Ok(result);
    }

    [HttpGet("adjustment-history")]
    [Authorize(Policy = PermissionCodes.Inventory.Adjustment.View)]
    public async Task<IActionResult> GetAdjustmentHistory(CancellationToken ct)
    {
        var result = await _inventoryService.GetAdjustmentHistoryAsync(ct);
        return Ok(result);
    }
    [HttpGet("balance-item")]
    [RequireAnyPermission(PermissionCodes.Inventory.Balance.View, PermissionCodes.Inventory.Adjustment.View)]
    public async Task<IActionResult> GetBalanceItem([FromQuery] int warehouseId, [FromQuery] int productVariantId, CancellationToken ct)
    {
        if (warehouseId <= 0)
            return BadRequest("WarehouseId không hợp lệ.");

        if (productVariantId <= 0)
            return BadRequest("ProductVariantId không hợp lệ.");

        var result = await _inventoryService.GetBalanceItemAsync(warehouseId, productVariantId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Danh sách tồn kho hiện tại.
    /// Phase 5.6.1
    /// </summary>
    [HttpGet("current-balances")]
    [Authorize(Policy = PermissionCodes.Inventory.Balance.View)]
    public async Task<IActionResult> GetCurrentBalances(
        [FromQuery] InventoryBalanceQueryRequest request,
        CancellationToken ct)
    {
        var result = await _inventoryService.GetCurrentBalancesAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Ledger / thẻ kho.
    /// Phase 5.6.2
    /// </summary>
    [HttpGet("ledger")]
    [Authorize(Policy = PermissionCodes.Inventory.Transaction.View)]
    public async Task<IActionResult> GetLedger(
        [FromQuery] InventoryLedgerQueryRequest request,
        CancellationToken ct)
    {
        var result = await _inventoryService.GetLedgerAsync(request, ct);
        return Ok(result);
    }
}