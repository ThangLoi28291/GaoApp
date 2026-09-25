using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Purchases;

public interface IPurchaseReceivingWorkbenchRepository
{
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
    Task<PurchaseOrder?> LockAndGetPurchaseOrderAsync(int storeId, int purchaseOrderId, CancellationToken ct = default);
    Task<StockDocument?> GetEditableForPurchaseOrderAsync(int storeId, int purchaseOrderId, CancellationToken ct = default);
    Task<StockDocument?> LockAndGetDocumentAsync(int storeId, int stockDocumentId, CancellationToken ct = default);
    Task<StockDocument?> GetDocumentAsync(int storeId, int stockDocumentId, bool tracking, CancellationToken ct = default);
    Task<ProductUnitConversion?> GetConversionAsync(int storeId, int conversionId, CancellationToken ct = default);
    Task<PurchaseReceivingAction?> GetCommandAsync(int storeId, int stockDocumentId, Guid commandId, CancellationToken ct = default);
    Task<PurchaseReceivingAction?> GetLatestUndoableAsync(int storeId, int stockDocumentId, int revision, CancellationToken ct = default);
    Task AddDocumentAsync(StockDocument document, CancellationToken ct = default);
    Task AddActionAsync(PurchaseReceivingAction action, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
