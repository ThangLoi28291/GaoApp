using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Purchases;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Purchases;

public interface IPurchaseOrderRepository
{
    Task<PagedResult<PurchaseOrder>> SearchAsync(
        string? search,
        int? legalEntityId,
        DateTime? fromDate,
        DateTime? toDate,
        IReadOnlyCollection<PurchaseOrderStatus>? statuses,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<Dictionary<PurchaseOrderStatus, int>> GetStatusCountsAsync(CancellationToken ct = default);
    Task<int> CountPendingApprovalAsync(CancellationToken ct = default);
    Task<PurchaseOrder?> GetDetailAsync(int id, bool tracking = false, CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, decimal>> GetInFlightReceiptQuantitiesAsync(
        int purchaseOrderId, IReadOnlyCollection<int> purchaseOrderLineIds,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<int, decimal>>(new Dictionary<int, decimal>());
    Task<Dictionary<int, string>> GetUserDisplayNamesAsync(IEnumerable<int> userIds, CancellationToken ct = default);
    Task AddAsync(PurchaseOrder entity, CancellationToken ct = default);
    Task<Supplier?> GetSupplierAsync(int id, CancellationToken ct = default);
    Task<Warehouse?> GetWarehouseAsync(int id, CancellationToken ct = default);
    Task<LegalEntity?> GetLegalEntityAsync(int id, CancellationToken ct = default);
    Task<ProductVariant?> GetVariantAsync(int id, CancellationToken ct = default);
    Task<ProductUnitConversion?> GetConversionAsync(int id, CancellationToken ct = default);
    Task<ProductUnitConversion?> GetDefaultProductConversionAsync(int productId, CancellationToken ct = default);
    Task<Category?> GetCategoryAsync(int id, CancellationToken ct = default);
    Task<Unit?> GetUnitAsync(int id, CancellationToken ct = default);
    Task<List<Category>> GetActiveCategoriesAsync(CancellationToken ct = default);
    Task<List<Unit>> GetActiveUnitsAsync(CancellationToken ct = default);
    Task<bool> HasReceiptLineAsync(int purchaseOrderLineId, CancellationToken ct = default);
    Task<Tax?> GetTaxAsync(int id, CancellationToken ct = default);
    Task<List<Supplier>> GetSuppliersByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
    Task<List<Warehouse>> GetWarehousesAsync(CancellationToken ct = default);
    Task<List<LegalEntity>> GetLegalEntitiesAsync(CancellationToken ct = default);
    Task<List<Tax>> GetTaxesAsync(CancellationToken ct = default);
    Task<List<ProductUnitConversion>> GetProductOptionsByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
    Task<PurchaseLookupPageDto<PurchaseSupplierLookupDto>> SearchSuppliersAsync(
        int storeId, string? term, int page, int pageSize, CancellationToken ct = default);
    Task<PurchaseLookupPageDto<PurchaseProductLookupDto>> SearchProductsAsync(
        int storeId, string? term, int? preferredSupplierId, int page, int pageSize,
        CancellationToken ct = default);
    Task<int> GetNextLineNumberAsync(int purchaseOrderId, CancellationToken ct = default);
    Task RemoveLineAsync(PurchaseOrderLine line, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
