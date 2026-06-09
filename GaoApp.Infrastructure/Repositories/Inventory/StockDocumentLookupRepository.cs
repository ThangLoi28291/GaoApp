using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

/// <summary>
/// Repository đọc dữ liệu lookup cho chứng từ kho.
/// </summary>
public sealed class StockDocumentLookupRepository : IStockDocumentLookupRepository
{
    private readonly AppDbContext _db;

    public StockDocumentLookupRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> VariantExistsAsync(int variantId, CancellationToken ct = default)
    {
        return await _db.ProductVariants
            .AsNoTracking()
            .AnyAsync(x => x.Id == variantId, ct);
    }

    public async Task<List<StockDocumentVariantUnitDto>> GetVariantUnitsAsync(
        int variantId,
        CancellationToken ct = default)
    {
        // Lấy variant + product + base unit để giữ behavior fallback như code cũ
        var variant = await _db.ProductVariants
            .AsNoTracking()
            .Include(x => x.Product)
                .ThenInclude(x => x.BaseUnit)
            .FirstOrDefaultAsync(x => x.Id == variantId, ct);

        if (variant == null)
            return new List<StockDocumentVariantUnitDto>();

        // Ưu tiên lấy danh sách conversion đang hoạt động
        var conversions = await _db.ProductUnitConversions
            .AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => x.ProductVariantId == variantId && x.IsActive)
            .OrderByDescending(x => x.IsBaseUnit)
            .ThenByDescending(x => x.IsDefaultForSale)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(x => new StockDocumentVariantUnitDto
            {
                ProductUnitConversionId = x.Id,
                UnitId = x.UnitId,
                UnitName = x.Unit.Name,
                Factor = x.Factor,
                IsBaseUnit = x.IsBaseUnit,
                IsDefaultForSale = x.IsDefaultForSale,
                IsActive = x.IsActive
            })
            .ToListAsync(ct);

        if (conversions.Count > 0)
            return conversions;

        // Fallback như logic cũ của controller:
        // chưa có ProductUnitConversion thì dùng BaseUnit của Product
        if (variant.Product?.BaseUnit == null)
            return new List<StockDocumentVariantUnitDto>();

        return new List<StockDocumentVariantUnitDto>
        {
            new StockDocumentVariantUnitDto
            {
                ProductUnitConversionId = 0,
                UnitId = variant.Product.BaseUnitId,
                UnitName = variant.Product.BaseUnit.Name,
                Factor = 1m,
                IsBaseUnit = true,
                IsDefaultForSale = true,
                IsActive = true
            }
        };
    }
}