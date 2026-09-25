using GaoApp.Application.Interfaces.Repositories.Taxes;
using GaoApp.Application.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Taxes;

public sealed class TaxRepository : ITaxRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;
    public TaxRepository(AppDbContext db) => _db = db;

    public Task<(IReadOnlyList<Tax> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
        => GetPagedAsync(storeId, search, status: null, page, pageSize, ct);

    public async Task<(IReadOnlyList<Tax> Items, int TotalItems)> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var q = _db.Set<Tax>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();

            if (_db.Database.IsRelational())
            {
                var accentInsensitiveSearch = normalizedSearch
                    .Replace('Đ', 'D')
                    .Replace('đ', 'd');

                q = q.Where(x =>
                    EF.Functions.Collate(
                        x.Code
                            .Replace("Đ", "D")
                            .Replace("đ", "d"),
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
                    x.Code.Contains(normalizedSearch) ||
                    x.Name.Contains(normalizedSearch));
            }
        }

        if (status.HasValue)
            q = q.Where(x => x.IsActive == status.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var counts = await _db.Set<Tax>()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .GroupBy(x => x.IsActive)
            .Select(group => new
            {
                IsActive = group.Key,
                Count = group.Count()
            })
            .ToListAsync(ct);

        var activeItems = counts
            .Where(x => x.IsActive)
            .Select(x => x.Count)
            .FirstOrDefault();

        var inactiveItems = counts
            .Where(x => !x.IsActive)
            .Select(x => x.Count)
            .FirstOrDefault();

        return (activeItems + inactiveItems, activeItems, inactiveItems);
    }

    public Task<Tax?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => _db.Set<Tax>().FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);

    public Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default)
        => _db.Set<Tax>().AnyAsync(x =>
            x.StoreId == storeId &&
            x.Code == code &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default)
        => _db.Set<Tax>().AnyAsync(x =>
            x.StoreId == storeId &&
            x.Name == name &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task AddAsync(Tax entity, CancellationToken ct = default)
        => _db.Set<Tax>().AddAsync(entity, ct).AsTask();
    public void Remove(Tax entity)
    => _db.Set<Tax>().Remove(entity);

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
