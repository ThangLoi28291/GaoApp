using GaoApp.Application.Interfaces.Repositories.AttributeValues;
using GaoApp.Application.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.AttributeValues;

public sealed class AttributeValueRepository : IAttributeValueRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;
    public AttributeValueRepository(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<AttributeValue> Items, int TotalItems)> GetPagedAsync(
    int storeId,
    int? attributeId,
    bool? status,
    string? search,
    int page,
    int pageSize,
    CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var q = _db.AttributeValues
            .AsNoTracking()
            .Include(x => x.Attribute)
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (attributeId.HasValue && attributeId.Value > 0)
            q = q.Where(x => x.AttributeId == attributeId.Value);

        if (status.HasValue)
            q = q.Where(x => x.Status == status.Value);

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
                        x.Code,
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch) ||
                    EF.Functions.Collate(
                        x.Name
                            .Replace("Đ", "D")
                            .Replace("đ", "d"),
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch) ||
                    EF.Functions.Collate(
                        x.Attribute.Name
                            .Replace("Đ", "D")
                            .Replace("đ", "d"),
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch));
            }
            else
            {
                q = q.Where(x =>
                    x.Code.Contains(normalizedSearch) ||
                    x.Name.Contains(normalizedSearch) ||
                    x.Attribute.Name.Contains(normalizedSearch));
            }
        }

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
        var counts = await _db.AttributeValues
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

    public Task<AttributeValue?> GetByIdAsync(int storeId, int id, CancellationToken ct = default)
        => _db.AttributeValues
            .Include(x => x.Attribute)
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id && !x.IsDeleted, ct);

    public Task<bool> ExistsCodeAsync(int storeId, int attributeId, string code, int? excludeId, CancellationToken ct = default)
        => _db.AttributeValues.AnyAsync(x =>
            x.StoreId == storeId && !x.IsDeleted &&
            x.AttributeId == attributeId &&
            x.Code == code &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task<bool> ExistsNameAsync(int storeId, int attributeId, string name, int? excludeId, CancellationToken ct = default)
        => _db.AttributeValues.AnyAsync(x =>
            x.StoreId == storeId && !x.IsDeleted &&
            x.AttributeId == attributeId &&
            x.Name == name &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);

    public Task AddAsync(AttributeValue entity, CancellationToken ct = default)
        => _db.AttributeValues.AddAsync(entity, ct).AsTask();


    public void Remove(AttributeValue entity)
    => _db.Set<AttributeValue>().Remove(entity);
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
    public async Task<bool> IsUsedAsync(
    int storeId,
    int id,
    CancellationToken ct = default)
    {
        // Nếu DbSet liên kết của bạn tên khác, đổi tại đây.
        // Mục tiêu: kiểm tra AttributeValue đã được gắn vào biến thể/sản phẩm chưa.

        return await _db.ProductVariantAttributeValues
            .AsNoTracking()
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.AttributeValueId == id &&
                !x.IsDeleted,
                ct);
    }
}
