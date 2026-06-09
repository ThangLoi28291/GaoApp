using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Repositories.Orders;

/// <summary>
/// Repo thao tác với POSShift qua EF.
///
/// Ghi chú:
/// - Code mới phải ưu tiên query theo Store + Terminal.
/// - Các method không có store/terminal chỉ là legacy để giữ tương thích tạm thời.
/// </summary>
public class POSShiftRepository : IPOSShiftRepository
{
    private readonly AppDbContext _db;

    public POSShiftRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Legacy: lấy một ca mở bất kỳ.
    /// Không nên dùng cho code mới vì có thể lấy nhầm ca của terminal khác.
    /// </summary>
    public Task<POSShift?> GetOpenShiftAsync(CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "Unsafe legacy method. Multi-terminal POS phải dùng GetOpenShiftAsync(storeId, terminalId, ct).");
    }

    /// <summary>
    /// Method chuẩn:
    /// lấy ca mở theo Store + Terminal.
    /// </summary>
    public async Task<POSShift?> GetOpenShiftAsync(
      int storeId,
      int terminalId,
      CancellationToken ct = default)
    {
        return await GetOpenShiftWithDetailsAsync(storeId, terminalId, ct);
    }

    public async Task<POSShift?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.POSShifts
            .Include(x => x.Warehouse)
            .Include(x => x.Terminal)
            .Include(x => x.CurrentOrder)
              .Include(x => x.CashDenominations)
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                !x.IsDeleted,
                ct);
    }

    public async Task AddAsync(POSShift shift, CancellationToken ct = default)
    {
        await _db.POSShifts.AddAsync(shift, ct);
    }

    public Task UpdateAsync(POSShift shift, CancellationToken ct = default)
    {
        _db.POSShifts.Update(shift);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddCashTransactionAsync(POSShiftCashTransaction transaction, CancellationToken ct = default)
    {
        await _db.POSShiftCashTransactions.AddAsync(transaction, ct);
    }

    public async Task<List<POSShiftCashTransaction>> GetCashTransactionsByShiftIdAsync(int shiftId, CancellationToken ct = default)
    {
        return await _db.POSShiftCashTransactions
            .AsNoTracking()
            .Where(x =>
                x.POSShiftId == shiftId &&
                !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task<(List<POSShift> Items, int Total)> QueryHistoryAsync(
        int storeId,
        int? userId,
        int? terminalId,
        POSShiftStatus? status,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        IQueryable<POSShift> q = _db.POSShifts
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Terminal)
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted);

        if (userId.HasValue)
            q = q.Where(x => x.OpenedByUserId == userId.Value);

        if (terminalId.HasValue)
            q = q.Where(x => x.TerminalId == terminalId.Value);

        if (status.HasValue)
            q = q.Where(x => x.Status == status.Value);

        if (fromUtc.HasValue)
            q = q.Where(x => x.OpenedAtUtc >= fromUtc.Value);

        if (toUtcExclusive.HasValue)
            q = q.Where(x => x.OpenedAtUtc < toUtcExclusive.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.OpenedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    // =========================
    // LEGACY METHODS
    // =========================

    public async Task<POSShift?> GetOpenShiftWithCurrentOrderAsync(CancellationToken ct = default)
    {
        return await _db.POSShifts
            .Include(x => x.CurrentOrder)
            .Include(x => x.Terminal)
            .Include(x => x.Warehouse)
            .FirstOrDefaultAsync(x =>
                x.Status == POSShiftStatus.Open &&
                !x.IsDeleted,
                ct);
    }

    public async Task<POSShift?> GetCurrentOpenShiftAsync(CancellationToken ct = default)
    {
        return await _db.POSShifts
            .Include(x => x.Warehouse)
            .Include(x => x.Terminal)
            .FirstOrDefaultAsync(x =>
                x.Status == POSShiftStatus.Open &&
                !x.IsDeleted,
                ct);
    }

    public async Task<POSShift?> GetOpenShiftWithWarehouseAsync(CancellationToken ct = default)
    {
        return await _db.POSShifts
            .Include(x => x.Warehouse)
            .Include(x => x.Terminal)
            .FirstOrDefaultAsync(x =>
                x.Status == POSShiftStatus.Open &&
                !x.IsDeleted,
                ct);
    }
    /// <summary>
    /// Lấy ca POS đang mở theo ShiftId + StoreId.
    /// Dùng cho tiếp quản / đóng hộ ca.
    /// </summary>
    public async Task<POSShift?> GetOpenShiftByIdAsync(
        int storeId,
        int shiftId,
        CancellationToken ct = default)
    {
        return await _db.POSShifts
            .Include(x => x.Warehouse)
            .Include(x => x.Terminal)
            .Include(x => x.CurrentOrder)
            .FirstOrDefaultAsync(x =>
                x.Id == shiftId &&
                x.StoreId == storeId &&
                x.Status == POSShiftStatus.Open &&
                !x.IsDeleted,
                ct);
    }
    /// <summary>
    /// Lấy ca POS đang mở theo Store + Terminal.
    /// Include sẵn Warehouse + Terminal + CurrentOrder.
    /// </summary>
    public async Task<POSShift?> GetOpenShiftWithDetailsAsync(
        int storeId,
        int terminalId,
        CancellationToken ct = default)
    {
        return await _db.POSShifts
            .Include(x => x.Warehouse)
            .Include(x => x.Terminal)
            .Include(x => x.CurrentOrder)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.TerminalId == terminalId &&
                x.Status == POSShiftStatus.Open &&
                !x.IsDeleted,
                ct);
    }

    /// <summary>
    /// Query danh sách ca cho dashboard quản lý.
    /// Include Terminal + Warehouse để hiển thị rõ ca thuộc máy/kho nào.
    /// </summary>
    public async Task<List<POSShift>> QueryForManagerDashboardAsync(
        int storeId,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int? userId,
        int? terminalId,
        POSShiftStatus? status,
        CancellationToken ct = default)
    {
        IQueryable<POSShift> q = _db.POSShifts
            .AsNoTracking()
            .Include(x => x.Terminal)
            .Include(x => x.Warehouse)
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted);

        if (fromUtc.HasValue)
            q = q.Where(x => x.OpenedAtUtc >= fromUtc.Value);

        if (toUtcExclusive.HasValue)
            q = q.Where(x => x.OpenedAtUtc < toUtcExclusive.Value);

        if (userId.HasValue)
            q = q.Where(x => x.OpenedByUserId == userId.Value);

        if (terminalId.HasValue)
            q = q.Where(x => x.TerminalId == terminalId.Value);

        if (status.HasValue)
            q = q.Where(x => x.Status == status.Value);

        return await q
            .OrderByDescending(x => x.OpenedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }


}