using GaoApp.Application.Interfaces.Repositories.ProductAttributes;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.ProductAttributes;

public sealed class ProductAttributeRepository : IProductAttributeRepository
{
    private readonly AppDbContext _db;
    public ProductAttributeRepository(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<ProductAttribute> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var q = _db.ProductAttributes.AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            q = q.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
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

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
