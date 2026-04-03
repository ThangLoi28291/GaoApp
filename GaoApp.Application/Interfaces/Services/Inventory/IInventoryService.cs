using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Inventory;

namespace GaoApp.Application.Interfaces.Services.Inventory;

/// <summary>
/// Service đọc dữ liệu kho:
/// - tồn kho hiện tại
/// - lịch sử giao dịch kho
/// - âm kho
/// - thẻ kho / ledger
///
/// Ghi chú kiến trúc barcode:
/// - ProductVariant không còn Barcode
/// - Nếu DTO cần hiển thị barcode, service sẽ resolve barcode đại diện
///   từ ProductUnitConversion + ProductVariantUnitBarcode
/// </summary>
public interface IInventoryService
{
    Task<List<InventoryBalanceDto>> GetBalancesByVariantAsync(int productVariantId, CancellationToken ct = default);

    Task<List<InventoryTransactionDto>> GetTransactionsByVariantAsync(int productVariantId, CancellationToken ct = default);

    Task<List<NegativeInventoryItemDto>> GetNegativeBalancesAsync(CancellationToken ct = default);

    Task<List<NegativeInventoryLogDto>> GetNegativeLogsAsync(CancellationToken ct = default);

    Task<List<StockAdjustmentHistoryItemDto>> GetAdjustmentHistoryAsync(CancellationToken ct = default);

    Task<NegativeInventoryItemDto?> GetBalanceItemAsync(
        int warehouseId,
        int productVariantId,
        CancellationToken ct = default);

    /// <summary>
    /// Service chuyên cho các màn hình tra cứu / đọc dữ liệu kho.
    /// Không dùng để ghi dữ liệu.
    /// </summary>
    Task<PagedResult<InventoryBalanceListItemDto>> GetCurrentBalancesAsync(
        InventoryBalanceQueryRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy thẻ kho / inventory ledger có filter, sort và phân trang.
    /// </summary>
    Task<PagedResult<InventoryLedgerItemDto>> GetLedgerAsync(
        InventoryLedgerQueryRequest request,
        CancellationToken ct = default);
}