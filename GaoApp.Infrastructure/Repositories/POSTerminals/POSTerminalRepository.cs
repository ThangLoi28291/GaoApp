using GaoApp.Application.Interfaces.Repositories.POSTerminals;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.POSTerminals;

public class POSTerminalRepository : IPOSTerminalRepository
{
    private readonly AppDbContext _db;

    public POSTerminalRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<POSTerminal?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.POSTerminals
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
    }

    public async Task<POSTerminal?> GetByStoreAndIpAsync(int storeId, string ip, CancellationToken ct = default)
    {
        return await _db.POSTerminals
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.LocalIp == ip &&
                x.IsActive &&
                x.Status == POSTerminalStatus.Active &&
                !x.IsDeleted, ct);
    }

    public async Task<List<POSTerminal>> GetActiveByStoreAsync(int storeId, CancellationToken ct = default)
    {
        return await _db.POSTerminals
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.IsActive &&
                x.Status == POSTerminalStatus.Active &&
                !x.IsDeleted)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);
    }
}