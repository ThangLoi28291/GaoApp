using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Application.Interfaces.Services.Inventory;

namespace GaoApp.Application.Services.Inventory;

/// <summary>
/// Service xử lý điều chỉnh kho thủ công.
/// Phase 5.15:
/// - adjustment increase dùng UnitCost thật
/// - adjustment decrease dùng ProvisionalUnitCost khi cần
/// </summary>
public class InventoryAdjustmentService : IInventoryAdjustmentService
{
    private readonly IWarehouseRepository _warehouseRepository;
    private readonly IInventoryUnitResolver _inventoryUnitResolver;
    private readonly IInventoryMovementService _inventoryMovementService;
    private readonly IInventoryMovementFactory _inventoryMovementFactory;
    private readonly IInventoryMovementNoteBuilder _inventoryMovementNoteBuilder;

    public InventoryAdjustmentService(
        IWarehouseRepository warehouseRepository,
        IInventoryUnitResolver inventoryUnitResolver,
        IInventoryMovementService inventoryMovementService,
        IInventoryMovementFactory inventoryMovementFactory,
        IInventoryMovementNoteBuilder inventoryMovementNoteBuilder)
    {
        _warehouseRepository = warehouseRepository;
        _inventoryUnitResolver = inventoryUnitResolver;
        _inventoryMovementService = inventoryMovementService;
        _inventoryMovementFactory = inventoryMovementFactory;
        _inventoryMovementNoteBuilder = inventoryMovementNoteBuilder;
    }

    public async Task<StockAdjustmentResultDto> CreateAsync(
        CreateStockAdjustmentRequest request,
        CancellationToken ct = default)
    {
        if (request.AdjustmentType != InventoryTransactionType.AdjustmentIncrease &&
            request.AdjustmentType != InventoryTransactionType.AdjustmentDecrease)
        {
            throw new InvalidOperationException("Loại điều chỉnh không hợp lệ.");
        }

        if (request.Quantity <= 0)
            throw new InvalidOperationException("Số lượng điều chỉnh phải lớn hơn 0.");

        var warehouse = await _warehouseRepository.GetByIdAsync(request.WarehouseId, ct);
        if (warehouse == null)
            throw new InvalidOperationException("Kho không tồn tại.");

        var unitInfo = await _inventoryUnitResolver.ResolveAsync(
            request.ProductVariantId,
            request.UnitId,
            ct);

        var factor = unitInfo.Factor <= 0 ? 1m : unitInfo.Factor;
        var inputQuantity = request.Quantity;
        var baseQuantity = inputQuantity * factor;

        var finalNote = _inventoryMovementNoteBuilder.BuildAdjustmentNote(
            request.Note,
            unitInfo.UnitName,
            inputQuantity,
            factor,
            baseQuantity);

        CreateInventoryMovementRequest movementRequest;

        if (request.AdjustmentType == InventoryTransactionType.AdjustmentIncrease)
        {
            if (!request.UnitCost.HasValue || request.UnitCost.Value <= 0)
                throw new InvalidOperationException("Điều chỉnh tăng bắt buộc phải có giá vốn (UnitCost) > 0.");

            movementRequest = _inventoryMovementFactory.CreateAdjustmentIncrease(
                request.WarehouseId,
                request.ProductVariantId,
                baseQuantity,
                request.UnitCost.Value,
                finalNote,
                DateTime.UtcNow);
        }
        else
        {
            movementRequest = _inventoryMovementFactory.CreateAdjustmentDecrease(
                request.WarehouseId,
                request.ProductVariantId,
                baseQuantity,
                request.ProvisionalUnitCost,
                finalNote,
                DateTime.UtcNow);
        }

        var movement = await _inventoryMovementService.CreateAsync(movementRequest, ct);

        return new StockAdjustmentResultDto
        {
            WarehouseId = request.WarehouseId,
            ProductVariantId = request.ProductVariantId,
            UnitId = unitInfo.UnitId,
            UnitName = unitInfo.UnitName,
            Factor = factor,
            InputQuantity = inputQuantity,
            BaseQuantity = baseQuantity,
            BeforeQty = movement.BeforeQty,
            QuantityChange = movement.QuantityChange,
            AfterQty = movement.AfterQty,
            IsNegativeAfterAdjustment = movement.IsNegativeAfterTransaction,
            Message = request.AdjustmentType == InventoryTransactionType.AdjustmentIncrease
                ? "Điều chỉnh tăng tồn thành công."
                : "Điều chỉnh giảm tồn thành công."
        };
    }
}