using GaoApp.Application.Interfaces.Repositories.ProductAttributes;
using GaoApp.Application.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.ProductAttributes;

public sealed class ProductAttributeRepository : IProductAttributeRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;
    public ProductAttributeRepository(AppDbContext db) => _db = db;

    public Task<(IReadOnlyList<ProductAttribute> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
        => GetPagedAsync(storeId, search, status: null, page, pageSize, ct);

    public async Task<(IReadOnlyList<ProductAttribute> Items, int TotalItems)> GetPagedAsync(
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

        var query = _db.ProductAttributes.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();

            if (_db.Database.IsRelational())
            {
                var accentInsensitiveSearch = normalizedSearch
                    .Replace('Đ', 'D')
                    .Replace('đ', 'd');

                query = query.Where(x =>
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
                query = query.Where(x =>
                    x.Code.Contains(normalizedSearch) ||
                    x.Name.Contains(normalizedSearch));
            }
        }

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var counts = await _db.ProductAttributes
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .GroupBy(x => x.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count()
            })
            .ToListAsync(ct);

        var activeItems = counts
            .Where(x => x.Status)
            .Select(x => x.Count)
            .FirstOrDefault();

        var inactiveItems = counts
            .Where(x => !x.Status)
            .Select(x => x.Count)
            .FirstOrDefault();

        return (activeItems + inactiveItems, activeItems, inactiveItems);
    }

    public Task<ProductAttribute?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => _db.ProductAttributes.FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id && !x.IsDeleted, ct);

    public Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default)
        => _db.ProductAttributes.AnyAsync(x =>
            x.StoreId == storeId && !x.IsDeleted &&
            x.Code == code && (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default)
        => _db.ProductAttributes.AnyAsync(x =>
            x.StoreId == storeId && !x.IsDeleted &&
            x.Name == name && (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task<List<ProductAttribute>> GetAllAsync(int storeId, CancellationToken ct = default)
        => _db.ProductAttributes.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .ToListAsync(ct);

    public Task AddAsync(ProductAttribute entity, CancellationToken ct = default)
        => _db.ProductAttributes.AddAsync(entity, ct).AsTask();

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
