using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;

    public ProductRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ProductListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var q = _db.Products
            .AsNoTracking()
            .Where(x => x.StoreId == storeId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            q = q.Where(x => x.Name.Contains(search) || x.Alias.Contains(search));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProductListItemDto
            {
                Id = x.Id,
                Name = x.Name,
                Alias = x.Alias,
                BasePrice = x.BasePrice,
                IsActive = x.IsActive,

                CategoryName = x.Category.Name,
                SupplierName = x.Supplier.Name,
                BaseUnitName = x.BaseUnit.Name,

                HasVariants = x.Variants.Any(),

                ImageCount = x.ProductImages.Count(pi => !pi.IsDeleted),

                PrimaryImageUrl = x.ProductImages
                    .Where(pi => !pi.IsDeleted)
                    .OrderByDescending(pi => pi.IsPrimary)
                    .ThenBy(pi => pi.SortOrder)
                    .Select(pi => "/" + pi.MediaAsset.StoragePath)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return new PagedResult<ProductListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items
        };
    }

    public Task<Product?> GetDetailAsync(int storeId, int id, CancellationToken ct = default)
    {
        return _db.Products
            .Include(x => x.Category)
            .Include(x => x.Supplier)
            .Include(x => x.Brand)
            .Include(x => x.Tax)
            .Include(x => x.BaseUnit)
            .Include(x => x.Variants)
            .Include(x => x.ProductImages)
                .ThenInclude(pi => pi.MediaAsset)
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);
    }

    public Task<bool> ExistsAliasAsync(
        int storeId,
        string alias,
        int? excludeId,
        CancellationToken ct = default)
    {
        var q = _db.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && x.Alias == alias);

        if (excludeId.HasValue)
            q = q.Where(x => x.Id != excludeId.Value);

        return q.AnyAsync(ct);
    }

    public Task AddAsync(Product entity, CancellationToken ct = default)
        => _db.Products.AddAsync(entity, ct).AsTask();

    /// <summary>
    /// Thêm base/unit conversion cho variant.
    /// Dùng trong flow tạo product mặc định.
    /// </summary>
    public Task AddProductUnitConversionAsync(ProductUnitConversion entity, CancellationToken ct = default)
    {
        _db.ProductUnitConversions.Add(entity);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Thêm barcode cho ProductUnitConversion.
    /// Dùng trong flow tạo product mặc định để POS quét được ngay.
    /// </summary>
    public Task AddProductVariantUnitBarcodeAsync(ProductVariantUnitBarcode entity, CancellationToken ct = default)
    {
        _db.ProductVariantUnitBarcodes.Add(entity);
        return Task.CompletedTask;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại và thử lại.",
                ex);
        }
    }

    public async Task<bool> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        var entity = await _db.Products
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);

        if (entity == null)
            return false;

        entity.IsActive = !entity.IsActive;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default)
    {
        var entity = await _db.Products
            .FirstOrDefaultAsync(x => x.StoreId == storeId && x.Id == id, ct);

        if (entity == null)
            return false;

        // AppDbContext của bạn đang convert Delete -> SoftDelete
        _db.Products.Remove(entity);

        await _db.SaveChangesAsync(ct);
        return true;
    }
    public Task<bool> ExistsVariantUnitBarcodeAsync(
    int storeId,
    string barcode,
    CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .IgnoreQueryFilters()
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.Barcode == barcode,
                ct);
    }
}