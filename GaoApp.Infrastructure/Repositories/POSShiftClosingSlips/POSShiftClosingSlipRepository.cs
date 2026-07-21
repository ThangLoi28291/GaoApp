using GaoApp.Application.Interfaces.Repositories.POSShiftClosingSlips;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.POSShiftClosingSlips;

public class POSShiftClosingSlipRepository : IPOSShiftClosingSlipRepository
{
    private readonly AppDbContext _db;

    public POSShiftClosingSlipRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(POSShiftClosingSlip slip, CancellationToken ct = default)
    {
        await _db.POSShiftClosingSlips.AddAsync(slip, ct);
    }

    public async Task<POSShiftClosingSlip?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
    {
        return await _db.POSShiftClosingSlips
            .Include(x => x.POSShift)
            .Include(x => x.Denominations)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == id &&
                !x.IsDeleted,
                ct);
    }

    public async Task<POSShiftClosingSlip?> GetByShiftIdAsync(int storeId, int shiftId, CancellationToken ct = default)
    {
        return await _db.POSShiftClosingSlips
            .Include(x => x.POSShift)
            .Include(x => x.Denominations)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.POSShiftId == shiftId &&
                !x.IsDeleted,
                ct);
    }

    public async Task<bool> ExistsSlipCodeAsync(int storeId, string slipCode, CancellationToken ct = default)
    {
        slipCode = slipCode.Trim();

        return await _db.POSShiftClosingSlips
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.SlipCode == slipCode &&
                !x.IsDeleted,
                ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
    }
}