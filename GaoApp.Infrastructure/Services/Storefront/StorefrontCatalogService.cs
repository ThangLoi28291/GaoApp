using GaoApp.Application.Services.Products;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontCatalogService(AppDbContext db) : IStorefrontCatalogService
{
    private int Store => db.CurrentStoreId ?? 0;

    public async Task<StorefrontCatalogPage> SearchAsync(StorefrontCatalogQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        StorefrontProductValidation.Page(query.Page, query.PageSize);
        StorefrontProductValidation.Require(Enum.IsDefined(query.Sort), "Thứ tự sắp xếp không hợp lệ.");
        StorefrontProductValidation.Require((query.Search?.Length ?? 0) <= 200 && (query.DisplayGroup?.Length ?? 0) <= 100, "Bộ lọc quá dài.");
        if (Store <= 0) return new(0, []);
        var source = StorefrontCatalogQueries.EligibleVariants(db, Store);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // CI_AI folds combining accents. Explicit D-stroke folding also covers Vietnamese đ/Đ.
            search = search.Replace('đ', 'd').Replace('Đ', 'D');
            source = source.Where(x => EF.Functions.Collate((x.Publication.DisplayName ?? x.Product.Name).Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(search) ||
                EF.Functions.Collate((x.Variant.ProductVariantName ?? x.Variant.Sku).Replace("đ", "d").Replace("Đ", "D"), "Latin1_General_100_CI_AI").Contains(search));
        }
        if (!string.IsNullOrWhiteSpace(query.DisplayGroup)) source = source.Where(x => x.Publication.DisplayGroup == query.DisplayGroup.Trim());
        var ids = source.Select(x => new { x.Product.Id, x.Publication.SortOrder, Name = x.Publication.DisplayName ?? x.Product.Name }).Distinct();
        var total = await ids.CountAsync(ct);
        var ordered = query.Sort switch
        {
            StorefrontCatalogSort.NameAsc => ids.OrderBy(x => x.Name).ThenBy(x => x.Id),
            StorefrontCatalogSort.NameDesc => ids.OrderByDescending(x => x.Name).ThenBy(x => x.Id),
            _ => ids.OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
        };
        var pageIds = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(x => x.Id).ToListAsync(ct);
        var loaded = await Load(pageIds, ct);
        return new(total, loaded.OrderBy(x => pageIds.IndexOf(x.ProductId)).ToList());
    }

    public async Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken ct = default) => Store <= 0 ? [] :
        await StorefrontCatalogQueries.EligibleVariants(db, Store).Where(x => x.Publication.DisplayGroup != null && x.Publication.DisplayGroup != "")
            .Select(x => x.Publication.DisplayGroup!).Distinct().OrderBy(x => x).Take(100).ToListAsync(ct);

    public async Task<StorefrontCatalogMediaSource?> GetMediaAsync(int mediaAssetId, CancellationToken ct = default)
    {
        if (Store <= 0 || mediaAssetId <= 0) return null;
        var visible = StorefrontCatalogQueries.EligibleVariants(db, Store).Select(x => x.Publication.Id).Distinct();
        var sources = await (from link in db.Set<GaoApp.Domain.Storefront.StorefrontMedia>().AsNoTracking()
            join asset in db.MediaAssets.AsNoTracking() on new { link.StoreId, Id = link.MediaAssetId } equals new { asset.StoreId, asset.Id }
            where link.StoreId == Store && asset.Id == mediaAssetId && visible.Contains(link.StorefrontProductId)
            select new { link.Kind, Asset = asset }).Distinct().Take(2).ToListAsync(ct);
        var source = sources.FirstOrDefault(x => StorefrontCatalogQueries.MediaPath(x.Asset, x.Kind) is not null);
        return source is null ? null : new(mediaAssetId, source.Kind, source.Asset.StoragePath, source.Asset.ContentType, source.Asset.SizeBytes);
    }

    public async Task<StorefrontCatalogProduct?> GetAsync(int productId, CancellationToken ct = default)
        => Store <= 0 || productId <= 0 ? null : (await Load([productId], ct)).SingleOrDefault();

    public async Task<StorefrontCatalogUnit?> ResolveAsync(int variantId, int unitId, CancellationToken ct = default)
    {
        if (Store <= 0 || variantId <= 0 || unitId <= 0) return null;
        var productId = await StorefrontCatalogQueries.EligibleVariants(db, Store).Where(x => x.Variant.Id == variantId)
            .Select(x => (int?)x.Product.Id).SingleOrDefaultAsync(ct);
        if (productId is null) return null;
        return (await GetAsync(productId.Value, ct))?.Variants.SingleOrDefault(x => x.VariantId == variantId)?.Units.SingleOrDefault(x => x.UnitId == unitId);
    }

    private async Task<IReadOnlyList<StorefrontCatalogProduct>> Load(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var rows = await StorefrontCatalogQueries.EligibleVariants(db, Store).Where(x => ids.Contains(x.Product.Id)).ToListAsync(ct);
        var variants = rows.Select(x => x.Variant.Id).ToList();
        var conversions = await db.ProductUnitConversions.AsNoTracking().Include(x => x.Unit)
            .Where(x => x.StoreId == Store && variants.Contains(x.ProductVariantId) && x.IsActive && x.Factor > 0 && x.Unit.StoreId == Store && x.Unit.IsActive).ToListAsync(ct);
        var sales = await db.StorefrontSellingUnits.AsNoTracking().Where(x => x.StoreId == Store && variants.Contains(x.VariantId) && x.IsPublished).ToListAsync(ct);
        var media = await (from link in db.Set<GaoApp.Domain.Storefront.StorefrontMedia>().AsNoTracking()
            join publication in db.StorefrontProducts.AsNoTracking() on new { link.StoreId, Id = link.StorefrontProductId } equals new { publication.StoreId, publication.Id }
            join asset in db.MediaAssets.AsNoTracking() on new { link.StoreId, Id = link.MediaAssetId } equals new { asset.StoreId, asset.Id }
            where link.StoreId == Store && ids.Contains(publication.ProductId)
            orderby link.SortOrder, link.Id
            select new { publication.ProductId, Link = link, Asset = asset }).ToListAsync(ct);
        var result = new List<StorefrontCatalogProduct>();
        foreach (var group in rows.GroupBy(x => x.Product.Id))
        {
            var first = group.First();
            var variantDtos = new List<StorefrontCatalogVariant>();
            foreach (var row in group)
            {
                row.Variant.Product = row.Product;
                var units = new List<StorefrontCatalogUnit>();
                if (row.VariantPublication.BaseUnitIsPublished && (!row.ByWeight || row.VariantPublication.BaseUnitWeightKg > 0))
                {
                    var conversion = StorefrontCatalogQueries.BaseConversion(row.Product, conversions.Where(x => x.ProductVariantId == row.Variant.Id));
                    units.Add(new(row.Variant.Id, row.BaseUnit.Id, conversion?.Id, row.BaseUnit.Name, 1,
                        ProductSalePricePolicy.Pos(row.Variant, conversion, wholesale: false), row.VariantPublication.BaseUnitWeightKg));
                }
                foreach (var conversion in conversions.Where(x => x.ProductVariantId == row.Variant.Id && x.UnitId != row.Product.BaseUnitId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
                {
                    var sale = sales.SingleOrDefault(x => x.StorefrontVariantId == row.VariantPublication.Id && x.VariantId == row.Variant.Id && x.ProductUnitConversionId == conversion.Id && x.UnitId == conversion.UnitId);
                    if (sale is null || row.ByWeight && sale.WeightKg is not > 0) continue;
                    units.Add(new(row.Variant.Id, conversion.UnitId, conversion.Id, conversion.Unit.Name, conversion.Factor,
                        ProductSalePricePolicy.Pos(row.Variant, conversion, wholesale: false), sale.WeightKg));
                }
                if (units.Count > 0) variantDtos.Add(new(row.Variant.Id, row.Variant.ProductVariantName ?? row.Variant.Sku, units));
            }
            if (variantDtos.Count == 0) continue;
            var mediaDtos = media.Where(x => x.ProductId == group.Key).Select(x => new { x.Link, Path = StorefrontCatalogQueries.MediaPath(x.Asset, x.Link.Kind) })
                .Where(x => x.Path is not null).Select(x => new StorefrontCatalogMedia(x.Link.MediaAssetId, x.Link.Kind,
                    "/shop/media/" + x.Link.MediaAssetId, x.Link.AltText)).ToList();
            result.Add(new(group.Key, string.IsNullOrWhiteSpace(first.Publication.DisplayName) ? first.Product.Name : first.Publication.DisplayName,
                first.Publication.Description, first.Publication.DisplayGroup, first.Publication.SortOrder, variantDtos, mediaDtos));
        }
        return result.OrderBy(x => x.SortOrder).ThenBy(x => x.ProductId).ToList();
    }
}
