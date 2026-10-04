using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed partial class ProductRepository : IProductRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

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
        => await GetPagedAsync(
            storeId,
            search,
            categoryId: null,
            isActive: null,
            isSellable: null,
            page,
            pageSize,
            ct);

    public async Task<PagedResult<ProductListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int? categoryId,
        bool? isActive,
        bool? isSellable,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        return await SearchCatalogAsync(storeId, search, categoryId, isActive, isSellable, page, pageSize, new(), ct);
    }

    public async Task<PagedResult<ProductListItemDto>> SearchCatalogAsync(int storeId, string? search, int? categoryId,
        bool? isActive, bool? isSellable, int page, int pageSize, ProductListFilters filters, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var q = _db.Products
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
                        x.Name
                            .Replace("Đ", "D")
                            .Replace("đ", "d"),
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch) ||
                    EF.Functions.Collate(
                        x.Alias
                            .Replace("Đ", "D")
                            .Replace("đ", "d"),
                        AccentInsensitiveSearchCollation).Contains(accentInsensitiveSearch) ||
                    x.Variants.Any(variant =>
                        variant.StoreId == storeId &&
                        (variant.Sku.Contains(normalizedSearch) ||
                         variant.UnitConversions.Any(conversion =>
                             conversion.StoreId == storeId &&
                             conversion.Barcodes.Any(barcode =>
                                 barcode.StoreId == storeId &&
                                 barcode.IsActive &&
                                 barcode.Barcode.Contains(normalizedSearch))))));
            }
            else
            {
                q = q.Where(x =>
                    x.Name.Contains(normalizedSearch) ||
                    x.Alias.Contains(normalizedSearch) ||
                    x.Variants.Any(variant =>
                        variant.StoreId == storeId &&
                        (variant.Sku.Contains(normalizedSearch) ||
                         variant.UnitConversions.Any(conversion =>
                             conversion.StoreId == storeId &&
                             conversion.Barcodes.Any(barcode =>
                                 barcode.StoreId == storeId &&
                                 barcode.IsActive &&
                                 barcode.Barcode.Contains(normalizedSearch))))));
            }
        }

        if (categoryId.HasValue)
            q = q.Where(x => x.CategoryId == categoryId.Value);

        if (isActive.HasValue)
            q = q.Where(x => x.IsActive == isActive.Value);

        if (isSellable.HasValue)
            q = q.Where(x => x.IsSellable == isSellable.Value);

        if (filters.SupplierId is > 0) q = q.Where(x => x.SupplierId == filters.SupplierId && x.Supplier.StoreId == storeId);
        if (filters.BrandId is > 0) q = q.Where(x => x.BrandId == filters.BrandId && x.Brand != null && x.Brand.StoreId == storeId);
        if (filters.BaseUnitId is > 0) q = q.Where(x => x.BaseUnitId == filters.BaseUnitId && x.BaseUnit.StoreId == storeId);
        if (filters.DataIssue == "no-brand") q = q.Where(x => x.BrandId == null);
        if (filters.DataIssue == "no-image") q = q.Where(x => !x.ProductImages.Any(i => i.StoreId == storeId && !i.IsDeleted && !i.MediaAsset.IsDeleted && i.MediaAsset.StoreId == storeId));
        if (filters.DataIssue == "no-barcode") q = q.Where(x => !x.Variants.Any(v => v.StoreId == storeId && v.IsActive &&
            v.UnitConversions.Any(c => c.StoreId == storeId && c.IsActive && c.Barcodes.Any(b => b.StoreId == storeId && b.IsActive))));

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
                IsSellable = x.IsSellable,

                CategoryName = x.Category.Name,
                SupplierName = x.Supplier.Name,
                BrandName = x.Brand == null ? null : x.Brand.Name,
                TaxName = x.Tax == null ? null : x.Tax.Name,
                BaseUnitName = x.BaseUnit.Name,

                HasVariants = x.Variants.Any(),
                VariantCount = x.Variants.Count(),

                ImageCount = x.ProductImages.Count(pi => !pi.IsDeleted),

                PrimaryImageUrl = x.ProductImages
                    .Where(pi => !pi.IsDeleted)
                    .OrderByDescending(pi => pi.IsPrimary)
                    .ThenBy(pi => pi.SortOrder)
                    .Select(pi => "/" + pi.MediaAsset.StoragePath)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        await PopulateSaleUnitsAsync(storeId, items, ct);

        return new PagedResult<ProductListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items
        };
    }

    public async Task<(int TotalItems, int PosAllowedItems, int NotForPosItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var counts = await _db.Products
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted)
            .GroupBy(x => new { x.IsActive, x.IsSellable })
            .Select(group => new
            {
                group.Key.IsActive,
                group.Key.IsSellable,
                Count = group.Count()
            })
            .ToListAsync(ct);

        var posAllowedItems = counts
            .Where(x => x.IsActive && x.IsSellable)
            .Sum(x => x.Count);

        var notForPosItems = counts
            .Where(x => x.IsActive && !x.IsSellable)
            .Sum(x => x.Count);

        var inactiveItems = counts
            .Where(x => !x.IsActive)
            .Sum(x => x.Count);

        return (
            posAllowedItems + notForPosItems + inactiveItems,
            posAllowedItems,
            notForPosItems,
            inactiveItems);
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
