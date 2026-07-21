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
    public async Task<IActionResult> CreateTransaction(
       [FromBody] CreateInventoryTransactionRequest request,
       CancellationToken ct)
    {
        var movementRequest = new CreateInventoryMovementRequest
        {
            WarehouseId = request.WarehouseId,
            ProductVariantId = request.ProductVariantId,
            QuantityChange = request.QuantityChange,
            TransactionType = request.TransactionType,
            ReferenceType = request.ReferenceType,
            ReferenceId = request.ReferenceId,
            ReferenceLineId = null, // nếu DTO cũ chưa có thì để null
            OccurredAtUtc = request.OccurredAtUtc,
            Note = request.Note,
            SkipIfExists = false
        };

        var result = await _inventoryMovementService.CreateAsync(movementRequest, ct);

        return Ok(result);
    }

    [HttpGet("balances/by-variant/{productVariantId:int}")]
    public async Task<IActionResult> GetBalancesByVariant(int productVariantId, CancellationToken ct)
    {
        var result = await _inventoryService.GetBalancesByVariantAsync(productVariantId, ct);
        return Ok(result);
    }

    [HttpGet("transactions/by-variant/{productVariantId:int}")]
    public async Task<IActionResult> GetTransactionsByVariant(int productVariantId, CancellationToken ct)
    {
        var result = await _inventoryService.GetTransactionsByVariantAsync(productVariantId, ct);
        return Ok(result);
    }

    [HttpGet("negative-balances")]
    public async Task<IActionResult> GetNegativeBalances(CancellationToken ct)
    {
        var result = await _inventoryService.GetNegativeBalancesAsync(ct);
        return Ok(result);
    }

    [HttpGet("negative-logs")]
    public async Task<IActionResult> GetNegativeLogs(CancellationToken ct)
    {
        var result = await _inventoryService.GetNegativeLogsAsync(ct);
        return Ok(result);
    }

    [HttpGet("adjustment-history")]
    public async Task<IActionResult> GetAdjustmentHistory(CancellationToken ct)
    {
        var result = await _inventoryService.GetAdjustmentHistoryAsync(ct);
        return Ok(result);
    }
    [HttpGet("balance-item")]
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
    public async Task<IActionResult> GetLedger(
        [FromQuery] InventoryLedgerQueryRequest request,
        CancellationToken ct)
    {
        var result = await _inventoryService.GetLedgerAsync(request, ct);
        return Ok(result);
    }
}