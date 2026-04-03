using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Inventory;

public sealed class InventoryMovementFactory : IInventoryMovementFactory
{
    public CreateInventoryMovementRequest CreatePurchaseReceipt(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        decimal unitCost,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(qtyBase, nameof(qtyBase));
        EnsurePositive(unitCost, nameof(unitCost));
        EnsureRequired(documentId, nameof(documentId));

        return BuildInbound(
            warehouseId,
            productVariantId,
            qtyBase,
            InventoryTransactionType.PurchaseReceipt,
            InventoryReferenceType.StockDocument,
            documentId,
            lineId,
            occurredAtUtc,
            $"Nhập kho từ phiếu {documentNo} - dòng #{lineNo}",
            unitCost);
    }

    public CreateInventoryMovementRequest CreateAdjustmentIncrease(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        decimal unitCost,
        string? note,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(qtyBase, nameof(qtyBase));
        EnsurePositive(unitCost, nameof(unitCost));

        // Phase 5.15:
        // Adjustment hiện tại chưa có document riêng, nhưng bảng InventoryTransactions
        // yêu cầu ReferenceId NOT NULL. Vì vậy phải sinh 1 mã tham chiếu tạm duy nhất
        // để trace được transaction nguồn và không vi phạm ràng buộc DB.
        var referenceId = GenerateManualAdjustmentReferenceId();

        return BuildInbound(
            warehouseId,
            productVariantId,
            qtyBase,
            InventoryTransactionType.AdjustmentIncrease,
            InventoryReferenceType.Adjustment,
            referenceId,
            null,
            occurredAtUtc,
            string.IsNullOrWhiteSpace(note) ? $"Điều chỉnh tăng tồn +{qtyBase}" : note,
            unitCost);
    }

    public CreateInventoryMovementRequest CreateAdjustmentDecrease(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        decimal? provisionalUnitCost,
        string? note,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(qtyBase, nameof(qtyBase));

        // Cùng nguyên tắc với adjustment increase:
        // ReferenceId không được null để audit / trace / query pending ổn định.
        var referenceId = GenerateManualAdjustmentReferenceId();

        return BuildOutbound(
            warehouseId,
            productVariantId,
            qtyBase,
            InventoryTransactionType.AdjustmentDecrease,
            InventoryReferenceType.Adjustment,
            referenceId,
            null,
            occurredAtUtc,
            string.IsNullOrWhiteSpace(note) ? $"Điều chỉnh giảm tồn -{qtyBase}" : note,
            provisionalUnitCost);
    }

    public CreateInventoryMovementRequest CreateSaleFinalize(
        int warehouseId,
        int productVariantId,
        int orderId,
        int lineId,
        string? itemName,
        decimal qtyBase,
        decimal? provisionalUnitCost,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(qtyBase, nameof(qtyBase));
        EnsurePositive(orderId, nameof(orderId));
        EnsurePositive(lineId, nameof(lineId));

        var item = string.IsNullOrWhiteSpace(itemName)
            ? $"variant #{productVariantId}"
            : itemName.Trim();

        return BuildOutbound(
            warehouseId,
            productVariantId,
            qtyBase,
            InventoryTransactionType.SaleIssue,
            InventoryReferenceType.Order,
            orderId.ToString(),
            lineId,
            occurredAtUtc,
            $"Xuất bán đơn #{orderId} - dòng #{lineId} - {item}",
            provisionalUnitCost);
    }

    public CreateInventoryMovementRequest CreateSaleVoid(
        int warehouseId,
        int productVariantId,
        int orderId,
        int lineId,
        decimal qtyBase,
        decimal unitCost,
        string reason,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(qtyBase, nameof(qtyBase));
        EnsurePositive(unitCost, nameof(unitCost));
        EnsurePositive(orderId, nameof(orderId));
        EnsurePositive(lineId, nameof(lineId));

        return BuildInbound(
            warehouseId,
            productVariantId,
            qtyBase,
            InventoryTransactionType.SaleVoidIn,
            InventoryReferenceType.Order,
            orderId.ToString(),
            lineId,
            occurredAtUtc,
            $"Hoàn nhập do void đơn #{orderId} - dòng #{lineId}. Lý do: {Normalize(reason)}",
            unitCost);
    }

    public CreateInventoryMovementRequest CreateSaleRefund(
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
        string? sourceReferenceSubKey = null)
    {
        if (baseQuantity <= 0)
            throw new InvalidOperationException("Base quantity phải > 0.");

        if (unitCost <= 0)
            throw new InvalidOperationException("Unit cost phải > 0.");

        return new CreateInventoryMovementRequest
        {
            WarehouseId = warehouseId,
            ProductVariantId = productVariantId,
            QuantityChange = baseQuantity,
            TransactionType = InventoryTransactionType.CustomerReturnIn,
            ReferenceType = InventoryReferenceType.Refund,
            ReferenceId = salesReturnId.ToString(),
            ReferenceLineId = salesReturnLineId,
            ReferenceSubKey = referenceSubKey,
            OccurredAtUtc = occurredAtUtc,
            Note = $"Nhập lại kho do trả hàng / hoàn tiền. Lý do: {reason}",
            SkipIfExists = true,
            UnitCost = unitCost,
            SourceValuationEntryId = sourceValuationEntryId,
            SourceReferenceSubKey = sourceReferenceSubKey
        };
    }

    public CreateInventoryMovementRequest CreateStockCountGain(
        int warehouseId,
        int productVariantId,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        decimal differenceQtyBase,
        decimal unitCost,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(differenceQtyBase, nameof(differenceQtyBase));
        EnsurePositive(unitCost, nameof(unitCost));
        EnsureRequired(documentId, nameof(documentId));

        return BuildInbound(
            warehouseId,
            productVariantId,
            differenceQtyBase,
            InventoryTransactionType.StockCountGain,
            InventoryReferenceType.StockCount,
            documentId,
            lineId,
            occurredAtUtc,
            $"Kiểm kê thừa từ phiếu {documentNo} - dòng #{lineNo}",
            unitCost);
    }

    public CreateInventoryMovementRequest CreateStockCountLoss(
        int warehouseId,
        int productVariantId,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        decimal differenceQtyBase,
        decimal? provisionalUnitCost,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(differenceQtyBase, nameof(differenceQtyBase));
        EnsureRequired(documentId, nameof(documentId));

        return BuildOutbound(
            warehouseId,
            productVariantId,
            differenceQtyBase,
            InventoryTransactionType.StockCountLoss,
            InventoryReferenceType.StockCount,
            documentId,
            lineId,
            occurredAtUtc,
            $"Kiểm kê thiếu từ phiếu {documentNo} - dòng #{lineNo}",
            provisionalUnitCost);
    }

    public CreateInventoryMovementRequest CreateTransferOutRequest(
        int fromWarehouseId,
        int productVariantId,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        decimal qtyBase,
        decimal unitCost,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(qtyBase, nameof(qtyBase));
        EnsurePositive(unitCost, nameof(unitCost));
        EnsureRequired(documentId, nameof(documentId));

        return BuildOutboundWithActualCost(
            fromWarehouseId,
            productVariantId,
            qtyBase,
            InventoryTransactionType.TransferOut,
            InventoryReferenceType.StockTransfer,
            documentId,
            lineId,
            occurredAtUtc,
            $"Chuyển kho xuất từ {documentNo} - dòng #{lineNo}",
            unitCost);
    }

    public CreateInventoryMovementRequest CreateTransferInRequest(
        int toWarehouseId,
        int productVariantId,
        string documentId,
        int lineId,
        string documentNo,
        int lineNo,
        decimal qtyBase,
        decimal unitCost,
        DateTime? occurredAtUtc = null)
    {
        EnsurePositive(qtyBase, nameof(qtyBase));
        EnsurePositive(unitCost, nameof(unitCost));
        EnsureRequired(documentId, nameof(documentId));

        return BuildInbound(
            toWarehouseId,
            productVariantId,
            qtyBase,
            InventoryTransactionType.TransferIn,
            InventoryReferenceType.StockTransfer,
            documentId,
            lineId,
            occurredAtUtc,
            $"Chuyển kho nhập từ {documentNo} - dòng #{lineNo}",
            unitCost);
    }

    private static CreateInventoryMovementRequest BuildInbound(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        InventoryTransactionType transactionType,
        InventoryReferenceType referenceType,
        string? referenceId,
        int? referenceLineId,
        DateTime? occurredAtUtc,
        string note,
        decimal unitCost)
    {
        return new CreateInventoryMovementRequest
        {
            WarehouseId = warehouseId,
            ProductVariantId = productVariantId,
            TransactionType = transactionType,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceLineId = referenceLineId,
            QuantityChange = Math.Abs(qtyBase),
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
            Note = note,
            UnitCost = unitCost,
            ProvisionalUnitCost = null,
            SkipIfExists = true
        };
    }

    private static CreateInventoryMovementRequest BuildOutbound(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        InventoryTransactionType transactionType,
        InventoryReferenceType referenceType,
        string? referenceId,
        int? referenceLineId,
        DateTime? occurredAtUtc,
        string note,
        decimal? provisionalUnitCost)
    {
        return new CreateInventoryMovementRequest
        {
            WarehouseId = warehouseId,
            ProductVariantId = productVariantId,
            TransactionType = transactionType,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceLineId = referenceLineId,
            QuantityChange = -Math.Abs(qtyBase),
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
            Note = note,
            UnitCost = null,
            ProvisionalUnitCost = provisionalUnitCost,
            SkipIfExists = true
        };
    }

    private static CreateInventoryMovementRequest BuildOutboundWithActualCost(
        int warehouseId,
        int productVariantId,
        decimal qtyBase,
        InventoryTransactionType transactionType,
        InventoryReferenceType referenceType,
        string? referenceId,
        int? referenceLineId,
        DateTime? occurredAtUtc,
        string note,
        decimal unitCost)
    {
        return new CreateInventoryMovementRequest
        {
            WarehouseId = warehouseId,
            ProductVariantId = productVariantId,
            TransactionType = transactionType,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReferenceLineId = referenceLineId,
            QuantityChange = -Math.Abs(qtyBase),
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
            Note = note,
            UnitCost = unitCost,
            ProvisionalUnitCost = null,
            SkipIfExists = true
        };
    }

    private static string GenerateManualAdjustmentReferenceId()
    {
        var shortGuid = Guid.NewGuid().ToString("N")[..6];
        return $"ADJ-{DateTime.UtcNow:yyyyMMddHHmmss}-{shortGuid}";
    }

    private static void EnsurePositive(decimal value, string name)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(name, $"{name} phải > 0.");
    }

    private static void EnsurePositive(int value, string name)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(name, $"{name} phải > 0.");
    }

    private static void EnsureRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} là bắt buộc.", name);
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Không có lý do" : value.Trim();
}