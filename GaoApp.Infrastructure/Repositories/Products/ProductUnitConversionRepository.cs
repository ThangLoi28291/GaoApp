using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

/// <summary>
/// Repository quản lý quy đổi đơn vị bán của ProductVariant.
/// </summary>
public class ProductUnitConversionRepository : IProductUnitConversionRepository
{
    private readonly AppDbContext _db;

    public ProductUnitConversionRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Lấy conversion theo Id, kèm navigation đủ dùng cho service.
    /// </summary>
    public async Task<ProductUnitConversion?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.Barcodes.Where(b => !b.IsDeleted))
            .Include(x => x.ProductVariant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.BaseUnit)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    /// <summary>
    /// Lấy toàn bộ conversion của 1 variant.
    /// Dùng cho màn hình quản lý conversion / service xử lý nghiệp vụ.
    /// </summary>
    public async Task<List<ProductUnitConversion>> GetByVariantIdAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.Barcodes.Where(b => !b.IsDeleted))
            .Where(x => x.ProductVariantId == productVariantId)
            .OrderByDescending(x => x.IsBaseUnit)
            .ThenByDescending(x => x.IsDefaultForSale)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lấy conversion theo variant + unit.
    /// Rất quan trọng cho POS / StockDocument / InventoryAdjustment.
    /// </summary>
    public async Task<ProductUnitConversion?> GetByVariantAndUnitAsync(int productVariantId, int unitId, CancellationToken ct = default)
    {
        return await _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.Barcodes.Where(b => !b.IsDeleted))
            .Include(x => x.ProductVariant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.BaseUnit)
            .FirstOrDefaultAsync(x =>
                x.ProductVariantId == productVariantId &&
                x.UnitId == unitId,
                ct);
    }

    /// <summary>
    /// Lấy conversion mặc định để bán.
    /// </summary>
    public async Task<ProductUnitConversion?> GetDefaultForSaleAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.Barcodes.Where(b => !b.IsDeleted))
            .Include(x => x.ProductVariant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.BaseUnit)
            .FirstOrDefaultAsync(x =>
                x.ProductVariantId == productVariantId &&
                x.IsDefaultForSale,
                ct);
    }

    /// <summary>
    /// Lấy conversion đơn vị gốc.
    /// </summary>
    public async Task<ProductUnitConversion?> GetBaseUnitAsync(int productVariantId, CancellationToken ct = default)
    {
        return await _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.Barcodes.Where(b => !b.IsDeleted))
            .Include(x => x.ProductVariant)
                .ThenInclude(v => v.Product)
                    .ThenInclude(p => p.BaseUnit)
            .FirstOrDefaultAsync(x =>
                x.ProductVariantId == productVariantId &&
                x.IsBaseUnit,
                ct);
    }

    public async Task<bool> ExistsByVariantAndUnitAsync(
        int productVariantId,
        int unitId,
        int? excludeId = null,
        CancellationToken ct = default)
    {
        return await _db.ProductUnitConversions.AnyAsync(x =>
            x.ProductVariantId == productVariantId &&
            x.UnitId == unitId &&
            (!excludeId.HasValue || x.Id != excludeId.Value), ct);
    }

    public async Task AddAsync(ProductUnitConversion entity, CancellationToken ct = default)
    {
        await _db.ProductUnitConversions.AddAsync(entity, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
}