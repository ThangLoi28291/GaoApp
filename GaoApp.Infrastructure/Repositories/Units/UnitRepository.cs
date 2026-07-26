using GaoApp.Application.Interfaces.Repositories.Units;
using GaoApp.Application.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Units;

public sealed class UnitRepository : IUnitRepository
{
    private readonly AppDbContext _db;
    public UnitRepository(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<Unit> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var q = _db.Set<Unit>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            q = q.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Unit?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => _db.Set<Unit>().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id && !x.IsDeleted, ct);

    public Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default)
        => _db.Set<Unit>().AnyAsync(x =>
            x.StoreId == storeId && !x.IsDeleted &&
            x.Code == code &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default)
        => _db.Set<Unit>().AnyAsync(x =>
            x.StoreId == storeId && !x.IsDeleted &&
            x.Name == name &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task AddAsync(Unit entity, CancellationToken ct = default)
        => _db.Set<Unit>().AddAsync(entity, ct).AsTask();

    public void Remove(Unit entity)
    => _db.Set<Unit>().Remove(entity);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException(
                "Dữ liệu đã được người khác thay đổi. Vui lòng tải lại và thử lại.");
        }
    }
}
