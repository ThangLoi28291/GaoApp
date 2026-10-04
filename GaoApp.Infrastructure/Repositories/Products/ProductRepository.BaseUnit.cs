using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed partial class ProductRepository
{
    public async Task SynchronizeBaseUnitAsync(int storeId, int productId, int unitId, CancellationToken ct = default)
    {
        var unit = await _db.Units.SingleOrDefaultAsync(x => x.StoreId == storeId && x.Id == unitId && !x.IsDeleted, ct);
        if (unit is null)
            throw new BusinessRuleException("Đơn vị cơ bản không hợp lệ hoặc không thuộc cửa hàng này.");

        // Include inactive variants and deleted conversions: the unit uniqueness index
        // also includes deleted rows. Do not move barcodes to a different conversion.
        var variants = await _db.ProductVariants.Where(x => x.StoreId == storeId && x.ProductId == productId && !x.IsDeleted)
            .ToListAsync(ct);
        var ids = variants.Select(x => x.Id).ToArray();
        var conversions = await _db.ProductUnitConversions.IgnoreQueryFilters()
            .Where(x => x.StoreId == storeId && ids.Contains(x.ProductVariantId)).ToListAsync(ct);
        var changes = new List<(ProductVariant Variant, ProductUnitConversion? Base)>();
        foreach (var variant in variants)
        {
            var rows = conversions.Where(x => x.ProductVariantId == variant.Id).ToList();
            var bases = rows.Where(x => !x.IsDeleted && x.IsBaseUnit).ToList();
            if (bases.Count > 1 || bases.Any(x => x.Factor != 1m))
                throw new BusinessRuleException($"Biến thể '{variant.Sku}' có cấu hình đơn vị gốc không hợp lệ. Cần kiểm tra quy đổi trước khi lưu.");
            var current = bases.SingleOrDefault();
            if (current?.UnitId == unitId) continue;
            if (rows.Any(x => x.UnitId == unitId && x.Id != current?.Id))
                throw new BusinessRuleException($"Không thể đổi đơn vị gốc của '{variant.Sku}' sang '{unit.Name}' vì đơn vị này đã có trong bảng quy đổi. Không thể tự chuyển tỷ lệ thùng/lốc thành đơn vị gốc.");
            changes.Add((variant, current));
        }

        var changedIds = changes.Select(x => x.Variant.Id).ToArray();
        if (changedIds.Length == 0) return;
        // Old documents retain their unit IDs and conversion snapshots. Changing the
        // stock base after use would give those quantities a different meaning.
        var used = await _db.OrderLines.IgnoreQueryFilters().AnyAsync(x => x.StoreId == storeId && changedIds.Contains(x.VariantId), ct)
            || await _db.StockDocumentLines.IgnoreQueryFilters().AnyAsync(x => changedIds.Contains(x.ProductVariantId), ct)
            || await _db.PurchaseOrderLines.IgnoreQueryFilters().AnyAsync(x => x.StoreId == storeId && x.ProductVariantId.HasValue && changedIds.Contains(x.ProductVariantId.Value), ct)
            || await _db.InventoryTransactions.IgnoreQueryFilters().AnyAsync(x => x.StoreId == storeId && changedIds.Contains(x.ProductVariantId), ct)
            || await _db.InventoryAdjustmentLines.IgnoreQueryFilters().AnyAsync(x => x.StoreId == storeId && changedIds.Contains(x.ProductVariantId), ct)
            || await _db.StockCountLines.IgnoreQueryFilters().AnyAsync(x => changedIds.Contains(x.ProductVariantId), ct)
            || await _db.InventoryBalances.IgnoreQueryFilters().AnyAsync(x => x.StoreId == storeId && changedIds.Contains(x.ProductVariantId) && (x.OnHandQty != 0 || x.ReservedQty != 0), ct);
        if (used)
            throw new BusinessRuleException("Không thể đổi đơn vị gốc: sản phẩm đã có chứng từ hoặc số dư tồn kho. Hãy giữ đơn vị gốc hiện tại và thêm đơn vị quy đổi trong mục Đơn vị/Barcode. Chưa lưu thay đổi sản phẩm.");

        foreach (var change in changes)
        {
            if (change.Base is not null)
                change.Base.UnitId = unitId;
            else
                _db.ProductUnitConversions.Add(new ProductUnitConversion
                {
                    StoreId = storeId, ProductVariantId = change.Variant.Id, UnitId = unitId,
                    Factor = 1m, IsBaseUnit = true, IsActive = true,
                    IsDefaultForSale = !conversions.Any(x => x.ProductVariantId == change.Variant.Id && !x.IsDeleted && x.IsDefaultForSale)
                });
        }
    }
}
