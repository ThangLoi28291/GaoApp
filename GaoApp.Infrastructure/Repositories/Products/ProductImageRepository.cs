
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed class ProductImageRepository : IProductImageRepository
{
    private readonly AppDbContext _db;
    public ProductImageRepository(AppDbContext db) => _db = db;

    public Task AddRangeAsync(IEnumerable<ProductImage> images, CancellationToken ct = default)
        => _db.ProductImages.AddRangeAsync(images, ct);

    public Task<List<ProductImage>> GetByProductIdAsync(int productId, int storeId, CancellationToken ct = default)
        => _db.ProductImages
            .Include(x => x.MediaAsset)
            .Where(x => x.StoreId == storeId && x.ProductId == productId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

    public void RemoveRange(IEnumerable<ProductImage> images)
        => _db.ProductImages.RemoveRange(images);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
    public Task AddAsync(ProductImage entity, CancellationToken ct = default)
    {
        return _db.ProductImages.AddAsync(entity, ct).AsTask();
    }

    public void Update(ProductImage entity)
    {
        _db.ProductImages.Update(entity);
    }

    public void Remove(ProductImage entity)
    {
        _db.ProductImages.Remove(entity);
    }
    public async Task<int> GetNextSortOrderAsync(int storeId, int productId, CancellationToken ct = default)
    {
        var max = await _db.ProductImages
            .Where(x => x.StoreId == storeId && x.ProductId == productId && !x.IsDeleted)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync(ct);

        return (max ?? -1) + 1;
    }
    public async Task<bool> SetPrimaryByIdAsync(int storeId, int productId, int id, CancellationToken ct = default)
{
    var affected = await _db.ProductImages
        .Where(x => x.StoreId == storeId && x.ProductId == productId && x.Id == id && !x.IsDeleted)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPrimary, true), ct);

    return affected > 0;
}

public Task<int?> GetFirstActiveIdAsync(int storeId, int productId, CancellationToken ct = default)
{
    return _db.ProductImages
        .Where(x => x.StoreId == storeId && x.ProductId == productId && !x.IsDeleted)
        .OrderBy(x => x.SortOrder)
        .Select(x => (int?)x.Id)
        .FirstOrDefaultAsync(ct);
}
    public Task UnsetPrimaryAsync(int storeId, int productId, CancellationToken ct = default)
    {
        // ✅ ATOMIC: clear primary trực tiếp trên DB, không tracking
        return _db.ProductImages
            .Where(x => x.StoreId == storeId
                     && x.ProductId == productId
                     && !x.IsDeleted
                     && x.IsPrimary)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPrimary, false), ct);
    }


public async Task MoveToFirstAsync(int storeId, int productId, int imageId, CancellationToken ct = default)
{
    // Lấy danh sách id theo SortOrder hiện tại (trừ ảnh target)
    var ids = await _db.ProductImages
        .Where(x => x.StoreId == storeId && x.ProductId == productId && !x.IsDeleted && x.Id != imageId)
        .OrderBy(x => x.SortOrder)
        .Select(x => x.Id)
        .ToListAsync(ct);

    // Đưa target lên đầu
    await _db.ProductImages
        .Where(x => x.StoreId == storeId && x.ProductId == productId && x.Id == imageId && !x.IsDeleted)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.SortOrder, 0), ct);

    // Dồn các ảnh còn lại xuống: 1..n
    for (int i = 0; i < ids.Count; i++)
    {
        var id = ids[i];
        var newOrder = i + 1;

        await _db.ProductImages
            .Where(x => x.StoreId == storeId && x.ProductId == productId && x.Id == id && !x.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SortOrder, newOrder), ct);
    }
}

    public void ClearTracking(int storeId, int productId)
    {
        var entries = _db.ChangeTracker.Entries<ProductImage>()
            .Where(e => e.Entity.StoreId == storeId && e.Entity.ProductId == productId)
            .ToList();

        foreach (var e in entries)
            e.State = EntityState.Detached;
    }


}
