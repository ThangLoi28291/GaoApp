using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Repositories.Categories;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Categories;

public class CategoryRepository : ICategoryRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;
    public CategoryRepository(AppDbContext db) => _db = db;

    public Task<PagedResult<Category>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
        => GetPagedAsync(storeId, search, status: null, page, pageSize, ct);

    public async Task<PagedResult<Category>> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var q = _db.Set<Category>()
            .AsNoTracking()
            .Include(x => x.Parent)
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();

            if (_db.Database.IsRelational())
            {
                var accentInsensitiveSearch = search
                    .Replace('Đ', 'D')
                    .Replace('đ', 'd');

                q = q.Where(x =>
                    EF.Functions.Collate(
                        x.Code,
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch) ||
                    EF.Functions.Collate(
                        x.Name
                            .Replace("Đ", "D")
                            .Replace("đ", "d"),
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch));
            }
            else
            {
                q = q.Where(x =>
                    x.Code.Contains(search) ||
                    x.Name.Contains(search));
            }
        }

        if (status.HasValue)
            q = q.Where(x => x.IsActive == status.Value);

        var total = await q.CountAsync(ct);

        var items = await q.OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Category>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items
        };
    }

    public async Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var q = _db.Set<Category>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        var totalItems = await q.CountAsync(ct);
        var activeItems = await q.CountAsync(x => x.IsActive, ct);

        return (
            totalItems,
            activeItems,
            totalItems - activeItems);
    }

    public Task<Category?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => _db.Set<Category>().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);

    public Task<bool> ExistsCodeAsync(int storeId, string code, int? ignoreId, CancellationToken ct = default)
        => _db.Set<Category>().AnyAsync(x => x.StoreId == storeId && x.Code == code && (!ignoreId.HasValue || x.Id != ignoreId.Value), ct);

    public Task<bool> ExistsNameAsync(int storeId, string name, int? ignoreId, CancellationToken ct = default)
        => _db.Set<Category>().AnyAsync(x => x.StoreId == storeId && x.Name == name && (!ignoreId.HasValue || x.Id != ignoreId.Value), ct);

    public async Task<int> CreateAsync(Category entity, CancellationToken ct = default)
    {
        _db.Set<Category>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<bool> UpdateAsync(Category entity, CancellationToken ct = default)
    {
        _db.Set<Category>().Update(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ToggleStatusAsync(int storeId, int id, int? userId, CancellationToken ct = default)
    {
        var e = await _db.Set<Category>().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);
        if (e == null) return false;

        e.IsActive = !e.IsActive; // BaseLookupStoreEntity :contentReference[oaicite:6]{index=6}
        e.UpdatedAtUtc = DateTime.UtcNow;
        e.UpdatedBy = userId;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SoftDeleteAsync(int storeId, int id, int? userId, CancellationToken ct = default)
    {
        var e = await _db.Set<Category>().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);
        if (e == null) return false;

        e.IsDeleted = true;               // BaseEntity :contentReference[oaicite:7]{index=7}
        e.DeletedAtUtc = DateTime.UtcNow; // BaseEntity :contentReference[oaicite:8]{index=8}
        e.DeletedBy = userId;             // BaseEntity :contentReference[oaicite:9]{index=9}

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<Category>> GetParentOptionsAsync(int storeId, int? excludeId, CancellationToken ct = default)
    {
        var q = _db.Set<Category>().AsNoTracking()
            .Where(x => x.StoreId == storeId);

        if (excludeId.HasValue)
            q = q.Where(x => x.Id != excludeId.Value);

        return await q.OrderBy(x => x.Name).ToListAsync(ct);
    }
}
