using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository thao tác với allocation giữa valuation entry và FIFO cost layer.
/// 
/// Đây là nguồn dữ liệu chính để:
/// - biết outbound đã consume layer nào
/// - biết provisional outbound nào còn mở
/// - biết allocation nào còn outstanding để return/void mirror đúng fragment
/// </summary>
public interface IInventoryCostLayerAllocationRepository
{
    /// <summary>
    /// Thêm 1 allocation.
    /// </summary>
    Task AddAsync(InventoryCostLayerAllocation entity, CancellationToken ct = default);

    /// <summary>
    /// Thêm nhiều allocation.
    /// </summary>
    Task AddRangeAsync(IEnumerable<InventoryCostLayerAllocation> entities, CancellationToken ct = default);

    /// <summary>
    /// Lấy allocation theo id.
    /// </summary>
    Task<InventoryCostLayerAllocation?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ allocation của 1 valuation entry.
    /// Dùng để trace fragment/layer đã consume.
    /// </summary>
    Task<List<InventoryCostLayerAllocation>> GetByValuationEntryIdAsync(
        int valuationEntryId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy các allocation provisional còn mở để inbound layer thật resolve dần theo FIFO thời gian outbound.
    /// </summary>
    Task<List<InventoryCostLayerAllocation>> GetOpenProvisionalAllocationsAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy các allocation nguồn của order line vẫn còn outstanding
    /// sau khi trừ các reverse allocations (void/return).
    /// Dùng cho SalesReturn / refund / mirror valuation fragment.
    /// </summary>
    Task<List<InventoryCostLayerAllocation>> GetOutstandingSourceAllocationsForOrderLineAsync(
        int orderId,
        int orderLineId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy các provisional allocations còn mở có lock/update intent.
    /// Chưa bắt buộc dùng ngay, nhưng nên có để hỗ trợ tránh race condition sau này.
    /// </summary>
    Task<List<InventoryCostLayerAllocation>> GetOpenProvisionalAllocationsForUpdateAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Lưu thay đổi.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}