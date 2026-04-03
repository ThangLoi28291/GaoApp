using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository thao tác với FIFO cost layer.
/// 
/// Đây là nguồn dữ liệu chính để:
/// - lấy các inbound layer còn qty
/// - consume layer theo FIFO
/// - resolve provisional outbound theo inbound layer thật
/// </summary>
public interface IInventoryCostLayerRepository
{
    /// <summary>
    /// Thêm 1 cost layer mới.
    /// </summary>
    Task AddAsync(InventoryCostLayer entity, CancellationToken ct = default);

    /// <summary>
    /// Thêm nhiều cost layer.
    /// </summary>
    Task AddRangeAsync(IEnumerable<InventoryCostLayer> entities, CancellationToken ct = default);

    /// <summary>
    /// Lấy layer theo id.
    /// </summary>
    Task<InventoryCostLayer?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy các layer còn số lượng để consume theo FIFO.
    /// Sắp xếp theo OccurredAtUtc rồi Id tăng dần.
    /// </summary>
    Task<List<InventoryCostLayer>> GetOpenLayersAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy các layer còn qty, có lock để tránh race condition khi nhiều request cùng consume.
    /// Chỉ implement nếu repo hiện tại của bạn có pattern hỗ trợ lock/query transaction phù hợp.
    /// Tạm thời có thể chưa dùng ngay, nhưng nên chuẩn bị sẵn interface.
    /// </summary>
    Task<List<InventoryCostLayer>> GetOpenLayersForUpdateAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Lưu thay đổi.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}