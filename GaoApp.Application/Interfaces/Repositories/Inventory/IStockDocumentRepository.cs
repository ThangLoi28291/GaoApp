using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IStockDocumentRepository
{
    Task AddAsync(StockDocument entity, CancellationToken ct = default);

    Task<StockDocument?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<StockDocument?> GetDetailAsync(int id, CancellationToken ct = default);

    Task<StockDocument?> GetForConfirmAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<PurchaseReceiptAuditEvent>?>
        GetPurchaseReceiptAuditEventsAsync(
            int stockDocumentId,
            CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PurchaseReceiptAuditEvent>?>(null);

    Task<StockDocumentLine?> GetLineByIdAsync(int lineId, CancellationToken ct = default);

    Task<int> GetNextLineNoAsync(int stockDocumentId, CancellationToken ct = default);

    Task<bool> WarehouseExistsAsync(int warehouseId, CancellationToken ct = default);

    Task<bool> SupplierExistsAsync(int supplierId, CancellationToken ct = default);

    Task<Supplier?> GetSupplierAsync(int supplierId, CancellationToken ct = default);

    Task<ProductVariant?> GetVariantForStockDocumentAsync(int productVariantId, CancellationToken ct = default);

    Task<ProductUnitConversion?> GetConversionAsync(int productVariantId, int unitId, CancellationToken ct = default);

    Task<ProductUnitConversion?> GetBaseConversionAsync(int productVariantId, CancellationToken ct = default);
    Task<Tax?> GetTaxAsync(int taxId, CancellationToken ct = default);
    Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default);
    Task<Dictionary<int, decimal>> GetLastPurchaseBaseUnitPricesBeforeVatAsync(
        IEnumerable<int> productVariantIds,
        CancellationToken ct = default);
    Task<bool> LockPurchasePriceHistoryVariantsAsync(
        int storeId,
        IReadOnlyCollection<int> productVariantIds,
        CancellationToken ct = default);
    Task<PurchaseOrder?> GetPurchaseOrderForReceiptAsync(int purchaseOrderId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, decimal>> GetInFlightPurchaseReceiptQuantitiesAsync(
        int storeId, int purchaseOrderId, IReadOnlyCollection<int> purchaseOrderLineIds,
        int? excludeStockDocumentId = null, int? excludeStockDocumentLineId = null,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<int, decimal>>(new Dictionary<int, decimal>());
    Task<PurchaseOrderReceiptState?> LockPurchaseOrderForReceiptAsync(
        int storeId, int purchaseOrderId, CancellationToken ct = default)
        => throw new NotSupportedException("This repository does not support purchase-order locks.");
    Task<IReadOnlyDictionary<int, PurchaseOrderLineAllocationState>> LockPurchaseOrderLinesAsync(
        int storeId, int purchaseOrderId, IReadOnlyCollection<int> purchaseOrderLineIds,
        CancellationToken ct = default)
        => throw new NotSupportedException("This repository does not support purchase allocation locks.");
    Task<bool> PurchaseReceiptLineSnapshotsBelongToStoreAsync(
        int storeId, int stockDocumentId, IReadOnlyCollection<int> stockDocumentLineIds,
        CancellationToken ct = default)
        => Task.FromResult(true);
    Task AddPurchasePayableAsync(PurchasePayable payable, CancellationToken ct = default);
    Task<bool> PurchasePayableExistsAsync(string sourceKey, CancellationToken ct = default);

    Task<InventoryBalance?> GetInventoryBalanceAsync(int warehouseId, int productVariantId, CancellationToken ct = default);

    Task AddInventoryBalanceAsync(InventoryBalance entity, CancellationToken ct = default);

    Task AddInventoryTransactionAsync(InventoryTransaction entity, CancellationToken ct = default);

   

    Task<List<StockDocument>> GetReceiptListAsync(CancellationToken ct = default);

    Task RemoveLineAsync(StockDocumentLine line, CancellationToken ct = default);

    Task<bool> ExistsInventoryTransactionByReferenceLineAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        int referenceLineId,
        CancellationToken ct = default);

    Task BeginTransactionAsync(CancellationToken ct = default);

    Task CommitTransactionAsync(CancellationToken ct = default);

    Task RollbackTransactionAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task MarkVariantsHasInputInvoiceAsync(
    IEnumerable<int> productVariantIds,
    int? userId,
    CancellationToken ct = default);

}

public sealed record PurchaseOrderLineAllocationState(
    int PurchaseOrderLineId, int LineNo, decimal OrderedQuantity,
    decimal ReceivedQuantity, decimal ShortClosedQuantity,
    decimal ConversionFactor);

public sealed record PurchaseOrderReceiptState(
    int PurchaseOrderId, int StoreId, PurchaseOrderStatus Status,
    int SupplierId, int ExpectedWarehouseId, int LegalEntityId);
