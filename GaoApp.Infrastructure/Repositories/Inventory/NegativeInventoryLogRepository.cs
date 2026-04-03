using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class NegativeInventoryLogRepository : INegativeInventoryLogRepository
{
    private readonly AppDbContext _db;

    public NegativeInventoryLogRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(NegativeInventoryLog entity, CancellationToken ct = default)
        => _db.Set<NegativeInventoryLog>().AddAsync(entity, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async Task<List<NegativeInventoryLog>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Set<NegativeInventoryLog>()
            .Include(x => x.Warehouse)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ToListAsync(ct);
    }
}