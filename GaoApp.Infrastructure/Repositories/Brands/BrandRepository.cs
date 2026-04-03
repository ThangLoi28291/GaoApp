using GaoApp.Application.Interfaces.Repositories.Brands;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Brands;

public sealed class BrandRepository : IBrandRepository
{
    private readonly AppDbContext _db;
    public BrandRepository(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<Brand> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var q = _db.Set<Brand>().AsNoTracking()
            .Where(x => x.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(x => x.Code.Contains(search) || x.Name.Contains(search));

        var total = await q.CountAsync(ct);

        var items = await q.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Brand?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => _db.Set<Brand>().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);

    public Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default)
        => _db.Set<Brand>().AnyAsync(x =>
            x.StoreId == storeId && x.Code == code &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default)
        => _db.Set<Brand>().AnyAsync(x =>
            x.StoreId == storeId && x.Name == name &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task AddAsync(Brand entity, CancellationToken ct = default)
        => _db.Set<Brand>().AddAsync(entity, ct).AsTask();

    public void Remove(Brand entity)
        => _db.Set<Brand>().Remove(entity);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
