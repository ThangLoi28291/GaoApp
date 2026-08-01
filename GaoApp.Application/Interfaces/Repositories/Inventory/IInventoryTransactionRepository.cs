using GaoApp.Application.DTOs.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

/// <summary>
/// Repository đọc/ghi transaction kho.
///
/// Ghi chú kiến trúc barcode:
/// - ProductVariant không còn Barcode
/// - Nếu cần filter keyword theo barcode thì repository phải đi qua:
///   ProductVariant -> UnitConversions -> Barcodes
/// </summary>
public interface IInventoryTransactionRepository
{
    Task AddAsync(InventoryTransaction transaction, CancellationToken ct = default);

    /// <summary>
    /// Thêm nhiều transaction trong cùng 1 nghiệp vụ.
    /// Dùng khi order có nhiều dòng hàng.
    /// </summary>
    Task AddRangeAsync(IEnumerable<InventoryTransaction> transactions, CancellationToken ct = default);

    Task<List<InventoryTransaction>> GetByVariantAsync(int productVariantId, CancellationToken ct = default);

    Task<InventoryTransaction?> GetByIdempotencyKeyAsync(
        int storeId,
        byte[] idempotencyKey,
        CancellationToken ct = default);

    Task<InventoryTransaction?> GetByLegacyIdentityAsync(
        int storeId,
        int warehouseId,
        int productVariantId,
        InventoryTransactionType transactionType,
        InventoryReferenceType referenceType,
        string referenceId,
        int? referenceLineId,
        string? referenceSubKey,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task<List<InventoryTransaction>> GetAdjustmentTransactionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Query thẻ kho / ledger kho có hỗ trợ filter, sort và phân trang.
    /// </summary>
    Task<(List<InventoryTransaction> Items, int TotalItems)> QueryLedgerAsync(
        InventoryLedgerQueryRequest request,
        CancellationToken ct = default);
}
