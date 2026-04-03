using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Inventory;

public interface IInventoryMovementFactory
{
    CreateInventoryMovementRequest CreatePurchaseReceipt(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        decimal unitCost,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        DateTime? occurredAtUtc = null);

    CreateInventoryMovementRequest CreateAdjustmentIncrease(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        decimal unitCost,
        string? note,
        DateTime? occurredAtUtc = null);

    CreateInventoryMovementRequest CreateAdjustmentDecrease(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        decimal? provisionalUnitCost,
        string? note,
        DateTime? occurredAtUtc = null);

    CreateInventoryMovementRequest CreateSaleFinalize(
        int warehouseId,
        int productVariantId,
        int orderId,
        int lineId,
        string? itemName,
        decimal qtyBase,
        decimal? provisionalUnitCost,
        DateTime? occurredAtUtc = null);

    CreateInventoryMovementRequest CreateSaleVoid(
        int warehouseId,
        int productVariantId,
        int orderId,
        int lineId,
        decimal qtyBase,
        decimal unitCost,
        string reason,
        DateTime? occurredAtUtc = null);

    CreateInventoryMovementRequest CreateSaleRefund(
     int warehouseId,
     int productVariantId,
     int salesReturnId,
     int salesReturnLineId,
     decimal baseQuantity,
     decimal unitCost,
     string reason,
     DateTime occurredAtUtc,
     string? referenceSubKey = null,
     int? sourceValuationEntryId = null,
     string? sourceReferenceSubKey = null);

    CreateInventoryMovementRequest CreateStockCountGain(
        int warehouseId,
        int productVariantId,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        decimal differenceQtyBase,
        decimal unitCost,
        DateTime? occurredAtUtc = null);

    CreateInventoryMovementRequest CreateStockCountLoss(
        int warehouseId,
        int productVariantId,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        decimal differenceQtyBase,
        decimal? provisionalUnitCost,
        DateTime? occurredAtUtc = null);

    CreateInventoryMovementRequest CreateTransferOutRequest(
        int fromWarehouseId,
        int productVariantId,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        decimal qtyBase,
        decimal unitCost,
        DateTime? occurredAtUtc = null);

    CreateInventoryMovementRequest CreateTransferInRequest(
        int toWarehouseId,
        int productVariantId,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        decimal qtyBase,
        decimal unitCost,
        DateTime? occurredAtUtc = null);
}