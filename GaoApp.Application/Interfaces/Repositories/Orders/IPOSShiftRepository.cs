using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Orders;

/// <summary>
/// Repository chuyên cho POS Shift.
/// Đặt ở Application để Service/UseCase không phụ thuộc Infrastructure.
///
/// Ghi chú:
/// - Code mới phải ưu tiên dùng method có storeId + terminalId.
/// - Các method không có store/terminal chỉ là legacy để giữ tương thích tạm thời.
/// </summary>
public interface IPOSShiftRepository
{
    /// <summary>
    /// Legacy: lấy một ca đang mở bất kỳ.
    /// Không nên dùng cho code mới vì không an toàn trong môi trường nhiều terminal.
    /// </summary>
    Task<POSShift?> GetOpenShiftAsync(CancellationToken ct = default);

    /// <summary>
    /// Method chuẩn:
    /// lấy ca mở theo đúng store + terminal hiện tại.
    /// </summary>
    Task<POSShift?> GetOpenShiftAsync(int storeId, int terminalId, CancellationToken ct = default);

    Task<POSShift?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(POSShift shift, CancellationToken ct = default);
    Task UpdateAsync(POSShift shift, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    Task AddCashTransactionAsync(POSShiftCashTransaction transaction, CancellationToken ct = default);
    Task<List<POSShiftCashTransaction>> GetCashTransactionsByShiftIdAsync(int shiftId, CancellationToken ct = default);

    Task<(List<POSShift> Items, int Total)> QueryHistoryAsync(
        int storeId,
        int? userId,
        int? terminalId,
        POSShiftStatus? status,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int page,
        int pageSize,
        CancellationToken ct = default);

    // =========================
    // LEGACY METHODS
    // =========================

    Task<POSShift?> GetOpenShiftWithCurrentOrderAsync(CancellationToken ct = default);
    Task<POSShift?> GetCurrentOpenShiftAsync(CancellationToken ct = default);
    Task<POSShift?> GetOpenShiftWithWarehouseAsync(CancellationToken ct = default);

    /// <summary>
    /// Lấy ca POS đang mở theo ShiftId + StoreId.
    /// Dùng cho luồng tiếp quản / đóng hộ ca.
    /// Include sẵn Terminal + Warehouse + CurrentOrder.
    /// </summary>
    Task<POSShift?> GetOpenShiftByIdAsync(
        int storeId,
        int shiftId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy ca POS đang mở theo Store + Terminal.
    /// Include sẵn Terminal + Warehouse + CurrentOrder.
    /// Method này dùng cho popup ownership và kiểm tra terminal.
    /// </summary>
    Task<POSShift?> GetOpenShiftWithDetailsAsync(
        int storeId,
        int terminalId,
        CancellationToken ct = default);
    /// <summary>
    /// Lấy danh sách ca POS cho dashboard quản lý.
    /// Không phân trang vì dashboard cần tổng hợp toàn bộ theo bộ lọc.
    /// </summary>
    Task<List<POSShift>> QueryForManagerDashboardAsync(
        int storeId,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int? userId,
        int? terminalId,
        POSShiftStatus? status,
        CancellationToken ct = default);


}