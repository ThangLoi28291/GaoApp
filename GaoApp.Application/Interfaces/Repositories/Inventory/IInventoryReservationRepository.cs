using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository xử lý dữ liệu reservation tồn kho.
///
/// Mục tiêu:
/// - lưu reservation active theo từng order/order line
/// - truy vấn reservation active để release/consume
/// - hỗ trợ chống tạo trùng reservation
/// </summary>
public interface IInventoryReservationRepository
{
    Task AddAsync(InventoryReservation entity, CancellationToken ct = default);

    Task AddRangeAsync(IEnumerable<InventoryReservation> entities, CancellationToken ct = default);

    /// <summary>
    /// Lấy tất cả reservation active theo chứng từ nguồn.
    /// Ví dụ: toàn bộ reservation active của 1 Order.
    /// </summary>
    Task<List<InventoryReservation>> GetActiveByReferenceAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra đã tồn tại reservation active cho đúng line/chứng từ/kho/variant hay chưa.
    /// Dùng để chống reserve trùng.
    /// </summary>
    Task<bool> ExistsActiveAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        int? referenceLineId,
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra chứng từ nguồn còn reservation active hay không.
    /// Ví dụ: order hiện còn reservation active để consume khi finalize.
    /// </summary>
    Task<bool> HasActiveByReferenceAsync(
        InventoryReferenceType referenceType,
        string referenceId,
        CancellationToken ct = default);



    Task SaveChangesAsync(CancellationToken ct = default);
}