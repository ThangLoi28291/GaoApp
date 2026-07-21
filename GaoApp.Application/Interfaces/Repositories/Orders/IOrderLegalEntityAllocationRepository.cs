using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

public interface IOrderLegalEntityAllocationRepository
{
    Task<Store?> GetStoreFeatureStateAsync(int storeId, CancellationToken ct = default);

    Task<List<LegalEntity>> GetActiveSalesLegalEntitiesAsync(
        int storeId,
        CancellationToken ct = default);

    /// <summary>
    /// Khóa update theo thứ tự ổn định cho toàn bộ cặp kho/variant trong transaction
    /// finalize hiện tại và trả snapshot balance đang được track.
    /// </summary>
    Task<List<InventoryBalance>> LockInventoryForAllocationAsync(
        int storeId,
        IReadOnlyCollection<int> warehouseIds,
        IReadOnlyCollection<int> productVariantIds,
        CancellationToken ct = default);

    Task<bool> AnyForOrderAsync(int orderId, CancellationToken ct = default);

    Task<List<OrderLegalEntityAllocation>> GetForOrderAsync(
        int orderId,
        CancellationToken ct = default);

    Task AddRangeAsync(
        IReadOnlyCollection<OrderLegalEntityAllocation> allocations,
        CancellationToken ct = default);
}
