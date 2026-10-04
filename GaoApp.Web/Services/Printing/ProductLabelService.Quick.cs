using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Printing;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Printing;

public sealed record QuickLabelSelection(int ConversionId, int Quantity, string Fingerprint);
public sealed record QuickLabelRefresh(List<int> ConversionIds);
public sealed record QuickLabelOption(int ConversionId, int ProductId, string Sku, string? ImageUrl,
    decimal Factor, bool IsBaseUnit, LabelProduct Product, string Fingerprint);

public sealed partial class ProductLabelService
{
    private IQueryable<ProductUnitConversion> QuickCatalog() => db.ProductUnitConversions.AsNoTracking()
        .Where(x => x.StoreId == StoreId && !x.IsDeleted && x.IsActive && x.Factor > 0 &&
            x.ProductVariant.StoreId == StoreId && !x.ProductVariant.IsDeleted && x.ProductVariant.IsActive &&
            x.ProductVariant.Product.StoreId == StoreId && !x.ProductVariant.Product.IsDeleted && x.ProductVariant.Product.IsActive &&
            x.Unit.StoreId == StoreId && !x.Unit.IsDeleted && x.Unit.IsActive);

    public async Task<List<QuickLabelOption>> QuickProducts(string? query, int? productId, int? variantId, CancellationToken ct)
    {
        query = query?.Trim() ?? "";
        if (query.Length > 100) throw new ValidationAppException("Tìm kiếm tối đa 100 ký tự.");
        if (query.Length == 0 && !productId.HasValue && !variantId.HasValue) return [];
        var catalog = QuickCatalog();
        if (productId.HasValue) catalog = catalog.Where(x => x.ProductVariant.ProductId == productId);
        if (variantId.HasValue) catalog = catalog.Where(x => x.ProductVariantId == variantId);
        if (query.Length > 0) catalog = catalog.Where(x => x.ProductVariant.Product.Name.Contains(query) ||
            x.ProductVariant.Product.Alias.Contains(query) || x.ProductVariant.ProductVariantName.Contains(query) ||
            x.ProductVariant.Sku.Contains(query) || x.Barcodes.Any(b => b.StoreId == StoreId && !b.IsDeleted && b.IsActive && b.Barcode == query));
        var ids = await catalog.OrderByDescending(x => x.Barcodes.Any(b => b.StoreId == StoreId && !b.IsDeleted && b.IsActive && b.Barcode == query))
            .ThenBy(x => x.ProductVariant.Product.Name).ThenBy(x => x.ProductVariantId).ThenByDescending(x => x.IsBaseUnit).ThenBy(x => x.Factor)
            .Select(x => x.Id).Take(60).ToListAsync(ct);
        return await QuickOptions(ids, ct);
    }

    private async Task<List<QuickLabelOption>> QuickOptions(List<int> ids, CancellationToken ct)
    {
        var units = await QuickCatalog().Where(x => ids.Contains(x.Id))
            .Include(x => x.Unit).Include(x => x.ProductVariant).ThenInclude(x => x.Product)
            .Include(x => x.Barcodes.Where(b => !b.IsDeleted && b.IsActive)).ToListAsync(ct);
        var productIds = units.Select(x => x.ProductVariant.ProductId).Distinct().ToArray();
        var images = await db.Set<ProductImage>().AsNoTracking()
            .Where(x => x.StoreId == StoreId && !x.IsDeleted && productIds.Contains(x.ProductId) && !x.MediaAsset.IsDeleted && x.MediaAsset.StoreId == StoreId)
            .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.SortOrder).ThenBy(x => x.Id)
            .Select(x => new { x.ProductId, x.MediaAsset.StoragePath }).ToListAsync(ct);
        return ids.Select(id => units.SingleOrDefault(x => x.Id == id)).Where(x => x is not null).Select(x =>
        {
            var v = x!.ProductVariant;
            var barcode = x.Barcodes.Where(b => b.StoreId == StoreId).OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).FirstOrDefault()?.Barcode ?? "";
            var price = x.Price is > 0 ? x.Price.Value : v.Price is > 0 ? v.Price.Value : v.Product.BasePrice;
            string? problem = null;
            try { ProductLabelRenderer.EncodeBarcode("AUTO", barcode); }
            catch (ValidationAppException e) { problem = e.Message; }
            if (price <= 0) problem = "Chưa có giá bán hợp lệ cho đơn vị này.";
            var product = new LabelProduct(v.Id, string.IsNullOrWhiteSpace(v.ProductVariantName) ? v.Product.Name : v.ProductVariantName,
                barcode, x.Unit.Name, price, 0, problem) { UnitId = x.UnitId };
            var imagePath = images.FirstOrDefault(i => i.ProductId == v.ProductId)?.StoragePath;
            return new QuickLabelOption(x.Id, v.ProductId, v.Sku, imagePath is null ? null : "/" + imagePath.TrimStart('/'), x.Factor, x.IsBaseUnit,
                product, Hash(new { product, x.Factor, ConversionVersion = Version(x), VariantVersion = Version(v), ProductVersion = Version(v.Product) }));
        }).ToList();
    }

    public Task<List<QuickLabelOption>> RefreshQuickProducts(QuickLabelRefresh request, CancellationToken ct)
    {
        if (request.ConversionIds is null || request.ConversionIds.Count > 500 || request.ConversionIds.Any(x => x <= 0))
            throw new ValidationAppException("Chọn tối đa 500 đơn vị sản phẩm.");
        return QuickOptions(request.ConversionIds.Distinct().ToList(), ct);
    }

    private async Task<List<LabelPrintItem>> QuickItems(List<QuickLabelSelection> lines, CancellationToken ct)
    {
        if (lines.Count is 0 or > 500 || lines.Any(x => x is null || x.ConversionId <= 0 || x.Quantity is < 1 or > 10000) ||
            lines.Sum(x => (long)x.Quantity) > 10000 || lines.Select(x => x.ConversionId).Distinct().Count() != lines.Count)
            throw new ValidationAppException("Chọn 1–500 đơn vị sản phẩm, không trùng đơn vị, tổng tối đa 10.000 tem.");
        var options = await QuickOptions(lines.Select(x => x.ConversionId).ToList(), ct);
        var items = new List<LabelPrintItem>();
        foreach (var line in lines)
        {
            var option = options.SingleOrDefault(x => x.ConversionId == line.ConversionId)
                ?? throw new NotFoundAppException("Sản phẩm hoặc đơn vị in không còn hoạt động trong cửa hàng.");
            if (option.Fingerprint != line.Fingerprint) throw new ConflictAppException("Giá bán hoặc sản phẩm đã thay đổi. Bấm Cập nhật giá rồi kiểm tra lại trước khi in.");
            if (option.Product.Problem is not null) throw new ValidationAppException(option.Product.Problem);
            items.Add(new(option.Product, line.Quantity));
        }
        return items;
    }
}
