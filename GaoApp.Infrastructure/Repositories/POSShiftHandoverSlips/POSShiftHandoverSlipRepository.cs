using GaoApp.Application.Interfaces.Repositories.POSShiftHandoverSlips;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.POSShiftHandoverSlips;

public class POSShiftHandoverSlipRepository : IPOSShiftHandoverSlipRepository
{
    private readonly AppDbContext _db;

    public POSShiftHandoverSlipRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(POSShiftHandoverSlip slip, CancellationToken ct = default)
    {
        await _db.POSShiftHandoverSlips.AddAsync(slip, ct);
    }

    public async Task<POSShiftHandoverSlip?> GetByIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        return await _db.POSShiftHandoverSlips
            .Include(x => x.Terminal)
            .Include(x => x.Warehouse)
            .Include(x => x.UsedPOSShift)
            .Include(x => x.Denominations)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == id &&
                !x.IsDeleted,
                ct);
    }

    public async Task<POSShiftHandoverSlip?> GetByBarcodeAsync(
        int storeId,
        string barcodeValue,
        CancellationToken ct = default)
    {
        barcodeValue = barcodeValue.Trim();

        return await _db.POSShiftHandoverSlips
            .Include(x => x.Terminal)
            .Include(x => x.Warehouse)
            .Include(x => x.UsedPOSShift)
            .Include(x => x.Denominations)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.BarcodeValue == barcodeValue &&
                !x.IsDeleted,
                ct);
    }

    public async Task<bool> ExistsSlipCodeAsync(
        int storeId,
        string slipCode,
        CancellationToken ct = default)
    {
        slipCode = slipCode.Trim();

        return await _db.POSShiftHandoverSlips
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.SlipCode == slipCode &&
                !x.IsDeleted,
                ct);
    }

    public async Task<(List<POSShiftHandoverSlip> Items, int Total)> QueryAsync(
        int storeId,
        POSShiftHandoverSlipStatus? status,
        int? terminalId,
        int? warehouseId,
        int? assignedToUserId,
        string? keyword,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        IQueryable<POSShiftHandoverSlip> q = _db.POSShiftHandoverSlips
            .AsNoTracking()
            .Include(x => x.Terminal)
            .Include(x => x.Warehouse)
            .Include(x => x.UsedPOSShift)
            .Include(x => x.Denominations)
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted);

        if (status.HasValue)
            q = q.Where(x => x.Status == status.Value);

        if (terminalId.HasValue)
            q = q.Where(x => x.TerminalId == terminalId.Value);

        if (warehouseId.HasValue)
            q = q.Where(x => x.WarehouseId == warehouseId.Value);

        if (assignedToUserId.HasValue)
            q = q.Where(x => x.AssignedToUserId == assignedToUserId.Value);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();

            q = q.Where(x =>
                x.SlipCode.Contains(keyword) ||
                x.BarcodeValue.Contains(keyword) ||
                (x.Note != null && x.Note.Contains(keyword)));
        }

        if (fromUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= fromUtc.Value);

        if (toUtcExclusive.HasValue)
            q = q.Where(x => x.CreatedAtUtc < toUtcExclusive.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
    }
    public async Task<POSShiftHandoverSlip?> GetUsableByIdAsync(
    int storeId,
    int id,
    CancellationToken ct = default)
    {
        return await _db.POSShiftHandoverSlips
            .Include(x => x.Terminal)
            .Include(x => x.Warehouse)
            .Include(x => x.Denominations)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == id &&
                !x.IsDeleted &&
                (
                    x.Status == POSShiftHandoverSlipStatus.Draft ||
                    x.Status == POSShiftHandoverSlipStatus.Printed
                ),
                ct);
    }

    public async Task<POSShiftHandoverSlip?> GetUsableByBarcodeAsync(
        int storeId,
        string barcodeValue,
        CancellationToken ct = default)
    {
        barcodeValue = barcodeValue.Trim();

        return await _db.POSShiftHandoverSlips
            .Include(x => x.Terminal)
            .Include(x => x.Warehouse)
            .Include(x => x.Denominations)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.BarcodeValue == barcodeValue &&
                !x.IsDeleted &&
                (
                    x.Status == POSShiftHandoverSlipStatus.Draft ||
                    x.Status == POSShiftHandoverSlipStatus.Printed
                ),
                ct);
    }
}