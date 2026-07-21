using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Purchases;

public interface IPurchaseRequestRepository
{
    Task<List<PurchaseRequest>> GetListAsync(int? requestedByUserId, CancellationToken ct = default);
    Task<PurchaseRequest?> GetDetailAsync(int id, bool tracking = false, CancellationToken ct = default);
    Task<PurchaseRequest?> GetDetailForConversionAsync(
        int id, int storeId, CancellationToken ct = default);
    Task AddAsync(PurchaseRequest entity, CancellationToken ct = default);
    Task AddPurchaseOrderAsync(PurchaseOrder entity, CancellationToken ct = default);
    Task<PurchaseOrder?> GetPurchaseOrderByConversionKeyAsync(
        int purchaseRequestId, string conversionKey, bool tracking = false, CancellationToken ct = default);
    Task<int> GetNextLineNumberAsync(int purchaseRequestId, CancellationToken ct = default);
    Task RemoveLineAsync(PurchaseRequestLine line, CancellationToken ct = default);
    Task<ProductVariant?> GetVariantAsync(int id, CancellationToken ct = default);
    Task<ProductUnitConversion?> GetConversionAsync(int id, CancellationToken ct = default);
    Task<Supplier?> GetSupplierAsync(int id, CancellationToken ct = default);
    Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default);
    Task<LegalEntity?> GetLegalEntityAsync(int id, CancellationToken ct = default);
    Task<Tax?> GetTaxAsync(int id, CancellationToken ct = default);
    Task<List<Warehouse>> GetWarehousesAsync(CancellationToken ct = default);
    Task<List<LegalEntity>> GetLegalEntitiesAsync(CancellationToken ct = default);
    Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default);
    Task<Dictionary<int, string>> GetUserDisplayNamesAsync(IEnumerable<int> userIds, CancellationToken ct = default);
    Task<Dictionary<int, PurchaseRequestInventoryContextDto>> GetInventoryContextAsync(
        int storeId, IReadOnlyCollection<int> productVariantIds, CancellationToken ct = default);
    Task<PurchaseLookupPageDto<PurchaseRequestProductLookupDto>> SearchProductsAsync(
        int storeId, string? term, int page, int pageSize, CancellationToken ct = default);
    Task<List<ProductUnitConversion>> GetProductOptionsByIdsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
