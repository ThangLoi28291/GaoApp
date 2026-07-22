using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

/// <summary>
/// Repository quản lý barcode theo đơn vị bán.
/// </summary>
public class ProductVariantUnitBarcodeRepository : IProductVariantUnitBarcodeRepository
{
    private readonly AppDbContext _db;

    public ProductVariantUnitBarcodeRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ProductVariantUnitBarcode?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.ProductVariantUnitBarcodes
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.Unit)
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.ProductVariant)
                    .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<List<ProductVariantUnitBarcode>> GetByConversionIdAsync(int productUnitConversionId, CancellationToken ct = default)
    {
        return await _db.ProductVariantUnitBarcodes
            .Where(x => x.ProductUnitConversionId == productUnitConversionId && !x.IsDeleted)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task<ProductVariantUnitBarcode?> GetPrimaryByConversionIdAsync(int productUnitConversionId, CancellationToken ct = default)
    {
        return await _db.ProductVariantUnitBarcodes
            .FirstOrDefaultAsync(x =>
                x.ProductUnitConversionId == productUnitConversionId &&
                x.IsPrimary &&
                !x.IsDeleted &&
                x.IsActive, ct);
    }

    public async Task<ProductVariantUnitBarcode?> FindByBarcodeAsync(
     int storeId,
     string barcode,
     CancellationToken ct = default)
    {
        var normalizedBarcode = NormalizeBarcode(barcode);
        if (normalizedBarcode is null)
        {
            return null;
        }

        return await _db.ProductVariantUnitBarcodes
            .AsNoTracking()
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.Unit)
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.ProductVariant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.BaseUnit)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.Barcode == normalizedBarcode &&
                !x.ProductUnitConversion.IsDeleted &&
                x.ProductUnitConversion.IsActive &&
                !x.ProductUnitConversion.ProductVariant.IsDeleted &&
                x.ProductUnitConversion.ProductVariant.IsActive &&
                !x.ProductUnitConversion.ProductVariant.Product.IsDeleted,
                ct);
    }

    public async Task<bool> ExistsBarcodeAsync(
     int storeId,
     string barcode,
     int? excludeId = null,
     CancellationToken ct = default)
    {
        var normalizedBarcode = NormalizeBarcode(barcode);
        if (normalizedBarcode is null)
        {
            return false;
        }

        return await _db.ProductVariantUnitBarcodes
            .IgnoreQueryFilters()
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.Barcode == normalizedBarcode &&
                (!excludeId.HasValue || x.Id != excludeId.Value),
                ct);
    }

    /// <summary>
    /// Bỏ cờ primary của các barcode khác trong cùng conversion.
    /// </summary>
    public async Task ClearPrimaryFlagsAsync(int productUnitConversionId, int? excludeId = null, CancellationToken ct = default)
    {
        var items = await _db.ProductVariantUnitBarcodes
            .Where(x =>
                x.ProductUnitConversionId == productUnitConversionId &&
                x.IsPrimary &&
                !x.IsDeleted &&
                (!excludeId.HasValue || x.Id != excludeId.Value))
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.IsPrimary = false;
        }
    }

    public async Task AddAsync(ProductVariantUnitBarcode entity, CancellationToken ct = default)
    {
        await _db.ProductVariantUnitBarcodes.AddAsync(entity, ct);
    }
    public async Task<ProductVariantUnitBarcode?> FindDuplicateWithDetailsAsync(
    int storeId,
    string barcode,
    int? excludeId = null,
    CancellationToken ct = default)
    {
        var normalizedBarcode = NormalizeBarcode(barcode);
        if (normalizedBarcode is null)
        {
            return null;
        }

        return await _db.ProductVariantUnitBarcodes
            .IgnoreQueryFilters()
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.Unit)
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.ProductVariant)
                    .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Barcode == normalizedBarcode &&
                (!excludeId.HasValue || x.Id != excludeId.Value),
                ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }

    private static string? NormalizeBarcode(string? barcode)
    {
        barcode = (barcode ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(barcode) ? null : barcode;
    }
    public Task<ProductVariantUnitBarcode?> GetActiveByBarcodeAsync(
        int storeId,
        string barcode,
        CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .AsNoTracking()
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.Unit)
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.ProductVariant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.BaseUnit)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.Barcode == barcode, ct);
    }

    public Task<ProductVariantUnitBarcode?> GetCurrentActiveForConversionAsync(
        int conversionId,
        CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .FirstOrDefaultAsync(x =>
                x.ProductUnitConversionId == conversionId &&
                !x.IsDeleted &&
                x.IsActive, ct);
    }

    public Task<ProductVariantUnitBarcode?> GetCurrentActiveForConversionForLookupAsync(
        int storeId,
        int conversionId,
        CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .AsNoTracking()
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.Unit)
            .Include(x => x.ProductUnitConversion)
                .ThenInclude(c => c.ProductVariant)
                    .ThenInclude(v => v.Product)
                        .ThenInclude(p => p.BaseUnit)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.ProductUnitConversionId == conversionId &&
                !x.IsDeleted &&
                x.IsActive, ct);
    }

    public Task<ProductUnitConversion?> GetConversionForChangeAsync(
        int conversionId,
        CancellationToken ct = default)
    {
        return _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.ProductVariant)
                .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(x =>
                x.Id == conversionId &&
                !x.IsDeleted, ct);
    }

    public Task<bool> ExistsActiveBarcodeAsync(
        int storeId,
        string barcode,
        int? excludeBarcodeId = null,
        CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .AnyAsync(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.Barcode == barcode &&
                (!excludeBarcodeId.HasValue || x.Id != excludeBarcodeId.Value), ct);
    }
    public Task<List<ProductVariantUnitBarcode>> GetByConversionIdAsync(
    int storeId,
    int conversionId,
    CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                x.ProductUnitConversionId == conversionId &&
                !x.IsDeleted)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.IsPrimary)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public Task<ProductUnitConversion?> GetConversionDetailAsync(
        int storeId,
        int conversionId,
        CancellationToken ct = default)
    {
        return _db.ProductUnitConversions
            .AsNoTracking()
            .Include(x => x.Unit)
            .Include(x => x.ProductVariant)
                .ThenInclude(v => v.Product)
            .FirstOrDefaultAsync(x =>
                x.StoreId == storeId &&
                x.Id == conversionId &&
                !x.IsDeleted, ct);
    }
    public Task<ProductVariantUnitBarcode?> GetPrimaryActiveByConversionIdAsync(
    int productUnitConversionId,
    CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .FirstOrDefaultAsync(x =>
                x.ProductUnitConversionId == productUnitConversionId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.IsPrimary,
                ct);
    }

    /// <summary>
    /// Lấy barcode active gần nhất để đôn lên làm mã chính.
    /// Quy ước "gần nhất":
    /// - cùng ProductUnitConversion
    /// - đang active
    /// - không phải barcode đang xử lý
    /// - ưu tiên Id lớn nhất (mới nhất)
    /// </summary>
    public Task<ProductVariantUnitBarcode?> GetNearestActiveCandidateAsync(
        int productUnitConversionId,
        int excludeBarcodeId,
        CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .Where(x =>
                x.ProductUnitConversionId == productUnitConversionId &&
                !x.IsDeleted &&
                x.IsActive &&
                x.Id != excludeBarcodeId)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }
}