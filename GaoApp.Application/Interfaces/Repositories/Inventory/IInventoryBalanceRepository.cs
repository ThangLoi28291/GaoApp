using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository đọc/ghi số dư tồn kho hiện tại.
///
/// Ghi chú kiến trúc barcode:
/// - ProductVariant không còn Barcode
/// - Nếu cần filter theo barcode thì repository phải đi qua:
///   ProductVariant -> UnitConversions -> Barcodes
/// </summary>
public interface IInventoryBalanceRepository
{
    Task<InventoryBalance?> GetByWarehouseAndVariantAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy balance nếu đã có, nếu chưa có thì tạo mới với OnHandQty = 0.
    /// Dùng rất tiện cho POS khi finalize / void / refund.
    /// </summary>
    Task<InventoryBalance> GetOrCreateAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    Task AddAsync(InventoryBalance balance, CancellationToken ct = default);

    Task<List<InventoryBalance>> GetByVariantAsync(
        int productVariantId,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<List<InventoryBalance>> GetNegativeBalancesAsync(CancellationToken ct = default);

    Task<InventoryBalance?> GetDetailByWarehouseAndVariantAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Query danh sách tồn kho hiện tại có hỗ trợ filter, sort và phân trang.
    /// Dùng cho màn hình tra cứu tồn kho tổng hợp.
    /// </summary>
    Task<(List<InventoryBalance> Items, int TotalItems)> QueryCurrentBalancesAsync(
        InventoryBalanceQueryRequest request,
        CancellationToken ct = default);
    Task<Dictionary<int, decimal>> GetAvailableQtyMapByVariantIdsAsync(
    int storeId,
    int warehouseId,
    IReadOnlyCollection<int> variantIds,
    CancellationToken ct = default);
}