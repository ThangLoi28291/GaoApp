using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

/// <summary>
/// Repository lookup barcode cho sản phẩm.
///
/// CHỐT KIẾN TRÚC:
/// - Không còn ProductVariant.Barcode
/// - Mọi barcode chính thức đều nằm ở ProductVariantUnitBarcode
/// - Vì vậy repository này chỉ lookup theo unit barcode
/// </summary>
public sealed class ProductBarcodeLookupRepository : IProductBarcodeLookupRepository
{
    private readonly AppDbContext _context;

    public ProductBarcodeLookupRepository(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lookup theo ProductVariantUnitBarcode.
    ///
    /// Trả về:
    /// - Product
    /// - ProductVariant
    /// - ProductUnitConversion
    /// - Unit hiện tại
    /// - Base unit
    /// - Giá bán resolve theo conversion/variant/product
    /// </summary>
    public async Task<BarcodeLookupResultDto?> FindByUnitBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        barcode = (barcode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(barcode))
            return null;

        var query =
            from ub in _context.ProductVariantUnitBarcodes.AsNoTracking()
            join conv in _context.ProductUnitConversions.AsNoTracking()
                on ub.ProductUnitConversionId equals conv.Id
            join pv in _context.ProductVariants.AsNoTracking()
                on conv.ProductVariantId equals pv.Id
            join p in _context.Products.AsNoTracking()
                on pv.ProductId equals p.Id
            join unit in _context.Units.AsNoTracking()
                on conv.UnitId equals unit.Id
            join baseUnit in _context.Units.AsNoTracking()
                on p.BaseUnitId equals baseUnit.Id
            where !ub.IsDeleted
                  && ub.IsActive
                  && ub.Barcode == barcode
                  && !conv.IsDeleted
                  && conv.IsActive
                  && !pv.IsDeleted
                  && pv.IsActive
                  && !p.IsDeleted
                  && p.IsActive
            select new BarcodeLookupResultDto
            {
                ProductId = p.Id,
                ProductName = p.Name,

                ProductVariantId = pv.Id,
                VariantSku = pv.Sku,

                ProductUnitConversionId = conv.Id,

                UnitId = unit.Id,
                UnitName = unit.Name,

                BaseUnitId = p.BaseUnitId,
                BaseUnitName = baseUnit.Name,

                Factor = conv.Factor <= 0 ? 1m : conv.Factor,
                IsBaseUnit = conv.IsBaseUnit,
                IsDefaultForSale = conv.IsDefaultForSale,

                CostPrice = pv.CostPrice,
                SellPrice = conv.Price ?? pv.Price ?? p.BasePrice,

                Barcode = ub.Barcode,
                SourceType = "UnitBarcode"
            };

        return await query.FirstOrDefaultAsync(ct);
    }
}