using GaoApp.Domain.Entities;

using GaoApp.Application.DTOs.Inventory.InputInvoices;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IInputInvoiceRepository
{
    Task<InputInvoiceResolution> ResolveInputInvoiceAsync(
        InputInvoiceHead entity,
        CancellationToken ct = default);

    Task<IReadOnlyList<InputInvoiceHead>> GetByBusinessIdentitiesAsync(
        int storeId,
        IReadOnlyCollection<InputInvoiceBusinessIdentityDto> identities,
        CancellationToken ct = default);

    Task<bool> IsStockDocumentInvoiceLinkedAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task BeginSupplierResolutionTransactionAsync(CancellationToken ct = default);
    Task CommitSupplierResolutionTransactionAsync(CancellationToken ct = default);
    Task RollbackSupplierResolutionTransactionAsync(CancellationToken ct = default);

    Task<InputInvoiceHead?> LockForSupplierResolutionAsync(
        int storeId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task<IReadOnlyList<StockDocument>> LockLinkedReceiptsForSupplierResolutionAsync(
        int storeId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task<StockDocument?> GetReceiptForSupplierResolutionAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task<StockDocument?> LockReceiptForInputInvoiceMutationAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task<StockDocument?> LockReceiptAggregateForSplitAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task<Supplier?> GetSplitSupplierAsync(
        int storeId,
        int supplierId,
        CancellationToken ct = default);

    Task<Warehouse?> GetSplitWarehouseAsync(
        int storeId,
        int warehouseId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Supplier>> GetSplitSuppliersAsync(
        int storeId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Warehouse>> GetSplitWarehousesAsync(
        int storeId,
        CancellationToken ct = default);

    Task<InputInvoiceHead?> GetLinkedInputInvoiceAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task<int> DeleteReceiptInputInvoiceLineMapsAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task<bool> DeleteStockDocumentInputInvoiceMapAsync(
        int storeId,
        int stockDocumentId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task<IReadOnlyList<InputInvoiceHead>> GetLinkedInvoicesForSupplierResolutionAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task<IReadOnlyList<InputInvoiceSupplierResolutionEvent>> GetSupplierResolutionEventsAsync(
        int storeId,
        int inputInvoiceHeadId,
        CancellationToken ct = default);

    Task AddSupplierResolutionEventAsync(
        InputInvoiceSupplierResolutionEvent resolutionEvent,
        CancellationToken ct = default);

    Task AddPurchaseReceiptAuditEventAsync(
        PurchaseReceiptAuditEvent auditEvent,
        CancellationToken ct = default);
    Task ClearFailedTransactionStateAsync(CancellationToken ct = default)
        => Task.CompletedTask;
    Task<InputInvoiceHead?> FindActiveByXmlHashAsync(
    int storeId,
    string xmlHash,
    CancellationToken ct = default);

    Task<bool> EnsureStockDocumentInvoiceMapAsync(
        StockDocumentInputInvoiceMap entity,
        CancellationToken ct = default);

    Task<bool> EnsureSingleReceiptInvoiceMapAsync(
        StockDocumentInputInvoiceMap entity,
        CancellationToken ct = default);

    Task<int> ResetReceiptLineMapsAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task<StockDocument?> GetStockDocumentWithLinesAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task AddMissingLineMapsAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
    void AssertLateAssociationMutationBoundary();
    Task<List<InputInvoiceHead>> GetByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default);
    Task<List<StockDocumentLineInputInvoiceMap>> GetLineMapsByStockDocumentAsync(
    int storeId,
    int stockDocumentId,
    CancellationToken ct = default);

    Task<StockDocumentLineInputInvoiceMap?> GetLineMapAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        CancellationToken ct = default);

    Task<InputInvoiceDetail?> GetInputInvoiceDetailAsync(
        int storeId,
        int stockDocumentId,
        int stockDocumentLineId,
        int inputInvoiceDetailId,
        CancellationToken ct = default);

    Task<StockDocumentLine?> GetStockDocumentLineAsync(
        int storeId,
        int stockDocumentLineId,
        CancellationToken ct = default);

    Task AcquireInputInvoiceItemCatalogKeyLockAsync(
        int storeId,
        int supplierId,
        string normalizedItemIdentity,
        string normalizedUnitName,
        CancellationToken ct = default);

    Task<InputInvoiceItemCatalogMap?> GetInputInvoiceItemCatalogMapAsync(
        int storeId,
        int supplierId,
        string? normalizedSupplierItemCode,
        string normalizedSupplierItemName,
        string normalizedSupplierUnitName,
        bool tracking,
        CancellationToken ct = default);

    Task<IReadOnlyList<InputInvoiceItemCatalogMap>>
        GetActiveInputInvoiceItemCatalogMapsByCodeAsync(
            int storeId,
            int supplierId,
            string normalizedSupplierItemCode,
            CancellationToken ct = default);

    Task<InputInvoiceItemCatalogTarget?> GetInputInvoiceItemCatalogTargetAsync(
        int storeId,
        int productVariantId,
        int productUnitConversionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<InputInvoiceItemCatalogTarget>>
        GetInputInvoiceItemCatalogTargetsByUnitAsync(
            int storeId,
            int productVariantId,
            string normalizedUnitName,
            CancellationToken ct = default);

    Task AddInputInvoiceItemCatalogMapAsync(
        InputInvoiceItemCatalogMap map,
        CancellationToken ct = default);

    Task<StockDocument?> LockReceiptForReconciliationAsync(
        int storeId,
        int stockDocumentId,
        CancellationToken ct = default);

    Task<List<StockDocumentInputInvoiceReconciliation>> GetReconciliationsAsync(
        int storeId,
        int stockDocumentId,
        bool tracking,
        CancellationToken ct = default);

    Task AddReconciliationAsync(
        StockDocumentInputInvoiceReconciliation reconciliation,
        CancellationToken ct = default);

    Task AddDetailReconciliationsAsync(
        IEnumerable<StockDocumentInputInvoiceDetailReconciliation> details,
        CancellationToken ct = default);

    Task<int> DeleteReconciliationsAsync(
        int storeId,
        int stockDocumentId,
        int? inputInvoiceHeadId = null,
        CancellationToken ct = default);
}

public sealed record InputInvoiceResolution(
    InputInvoiceHead Invoice,
    bool IsExisting);

public sealed record InputInvoiceItemCatalogTarget(
    ProductVariant Variant,
    ProductUnitConversion Conversion,
    Product Product,
    Unit Unit,
    Unit BaseUnit);
