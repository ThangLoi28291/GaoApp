using GaoApp.Application.DTOs.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Products;

public sealed partial class ReceiptSellingPriceService
{
    public async Task<List<ReceiptRetailComparisonDto>> GetRetailComparisonAsync(int documentId, CancellationToken ct)
    {
        await DocumentAsync(documentId, ct);
        var lines = await db.StockDocumentLines.AsNoTracking()
            .Where(x => x.StockDocumentId == documentId && !x.IsDeleted)
            .Select(x => new { x.Id, x.ProductVariantId, x.ProductUnitConversionId, x.UnitId, x.Factor }).ToListAsync(ct);
        var ids = lines.Select(x => x.ProductVariantId).Distinct().ToArray();
        var variants = await db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Where(x => ids.Contains(x.Id) && x.StoreId == StoreId && !x.IsDeleted &&
                x.Product.StoreId == StoreId && !x.Product.IsDeleted).ToDictionaryAsync(x => x.Id, ct);
        var units = await db.ProductUnitConversions.AsNoTracking().Include(x => x.Unit)
            .Where(x => ids.Contains(x.ProductVariantId) && x.StoreId == StoreId && !x.IsDeleted && x.IsActive &&
                x.Factor > 0 && x.Unit.StoreId == StoreId && !x.Unit.IsDeleted)
            .OrderBy(x => x.Id).ToListAsync(ct);
        var byVariant = units.ToLookup(x => x.ProductVariantId);
        return lines.Select(line =>
        {
            if (!variants.TryGetValue(line.ProductVariantId, out var variant))
                return new ReceiptRetailComparisonDto(line.Id, line.ProductVariantId, 0, "", null);
            var choices = byVariant[variant.Id].ToList();
            var unit = choices.FirstOrDefault(x => x.Id == line.ProductUnitConversionId)
                ?? choices.FirstOrDefault(x => x.UnitId == line.UnitId && x.Factor == line.Factor)
                ?? choices.FirstOrDefault(x => x.UnitId == variant.Product.BaseUnitId && x.Factor == 1);
            return new ReceiptRetailComparisonDto(line.Id, variant.Id, unit?.Id ?? 0,
                unit?.Unit.Name ?? variant.Product.BaseUnit?.Name ?? "Đơn vị gốc", Positive(EffectivePrice(unit, variant)));
        }).ToList();
    }
}
