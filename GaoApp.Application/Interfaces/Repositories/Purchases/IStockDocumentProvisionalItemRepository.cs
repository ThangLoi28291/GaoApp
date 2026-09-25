using GaoApp.Domain.Entities;
using GaoApp.Application.DTOs.Purchases;

namespace GaoApp.Application.Interfaces.Repositories.Purchases;

public interface IStockDocumentProvisionalItemRepository
{
    Task<StockDocument?> LockAndGetDocumentAsync(int storeId, int stockDocumentId, CancellationToken ct = default);
    Task<StockDocument?> GetDocumentAsync(int storeId, int stockDocumentId, CancellationToken ct = default);
    Task<StockDocumentProvisionalItem?> GetItemAsync(int storeId, int stockDocumentId, int itemId, CancellationToken ct = default);
    Task<StockDocumentProvisionalItem?> FindAccumulationTargetAsync(int storeId, int stockDocumentId, string normalizedBarcode, int? unitId, string? normalizedUnitName, CancellationToken ct = default);
    Task<ProductUnitConversion?> GetConversionAsync(int storeId, int variantId, int conversionId, CancellationToken ct = default);
    Task<Unit?> GetUnitAsync(int storeId, int unitId, CancellationToken ct = default);
    Task<IReadOnlyList<ProvisionalBarcodeEvidence>> GetBarcodeEvidenceAsync(
        int storeId, string normalizedBarcode, CancellationToken ct = default);
    Task<PurchaseReceivingAction?> GetCommandAsync(int storeId, int stockDocumentId, Guid commandId, CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptIntakeRecentDto>> GetRecentReceiptsAsync(int storeId, int stockDocumentId, CancellationToken ct = default);
    Task<PurchaseReceivingAction?> GetLatestProvisionalUndoableAsync(int storeId, int stockDocumentId, int revision, CancellationToken ct = default);
    Task<int> GetNextLineNoAsync(int storeId, int stockDocumentId, CancellationToken ct = default);
    Task AddItemAsync(StockDocumentProvisionalItem item, CancellationToken ct = default);
    Task AddActionAsync(PurchaseReceivingAction action, CancellationToken ct = default);
    Task AddAuditAsync(PurchaseReceiptAuditEvent auditEvent, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record ProvisionalBarcodeEvidence(
    int? BarcodeRecordId,
    int ProductUnitConversionId,
    bool IsActiveRecord,
    bool IsHistorical);
