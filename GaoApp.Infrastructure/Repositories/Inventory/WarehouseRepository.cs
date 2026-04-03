using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public class WarehouseRepository : IWarehouseRepository
{
    private readonly AppDbContext _db;

    public WarehouseRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Warehouse warehouse, CancellationToken ct = default)
    {
        await _db.Warehouses.AddAsync(warehouse, ct);
    }

    public async Task<Warehouse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<Warehouse?> GetDefaultAsync(CancellationToken ct = default)
    {
        return await _db.Warehouses
            .Where(x => x.IsDefault && x.IsActive)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<Warehouse>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Warehouses
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken ct = default)
    {
        return await _db.Warehouses.AnyAsync(x =>
            x.Code == code &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);
    }

    public async Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken ct = default)
    {
        return await _db.Warehouses.AnyAsync(x =>
            x.Name == name &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
    }
    public async Task ClearDefaultAsync(int? exceptWarehouseId = null, CancellationToken ct = default)
    {
        var query = _db.Warehouses
            .Where(x => x.IsDefault);

        if (exceptWarehouseId.HasValue && exceptWarehouseId.Value > 0)
        {
            query = query.Where(x => x.Id != exceptWarehouseId.Value);
        }

        var warehouses = await query.ToListAsync(ct);

        foreach (var warehouse in warehouses)
        {
            warehouse.IsDefault = false;
        }
    }
}