using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Products;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Infrastructure.Services.Storefront.StorefrontProductValidation;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed class StorefrontProductService(AppDbContext db, ICurrentUser user,
    ICurrentStorePermissionService permissions) : IStorefrontProductService
{
    private int Store => db.CurrentStoreId is > 0 ? db.CurrentStoreId.Value : throw new ForbiddenAppException("Chưa xác định cửa hàng.");
    private async Task Guard(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId is not > 0 ||
            !await permissions.HasPermissionAsync(Store, user.UserId.Value, permission, ct) ||
            !await db.Stores.AnyAsync(x => x.Id == Store && x.IsActive, ct))
            throw new ForbiddenAppException("Không có quyền quản lý hàng website tại cửa hàng này.");
    }
    private Task<bool> CanManage(CancellationToken ct) => permissions.HasPermissionAsync(Store, user.UserId!.Value, StorefrontPermissions.ManageProducts, ct);

    public async Task<StorefrontProductListPage> ListAsync(StorefrontProductQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await Guard(StorefrontPermissions.ViewProducts, ct);
        Page(query.Page, query.PageSize); Validate(query);
        Require(Enum.IsDefined(query.Publication), "Bộ lọc trạng thái không hợp lệ.");
        var source = from product in db.Products.AsNoTracking()
            join publication in db.StorefrontProducts.AsNoTracking() on new { product.StoreId, ProductId = product.Id } equals new { publication.StoreId, publication.ProductId } into publications
            from publication in publications.DefaultIfEmpty()
            where product.StoreId == Store
            select new { Product = product, Publication = publication };
        if (query.Publication == StorefrontPublicationFilter.Published) source = source.Where(x => x.Publication != null && x.Publication.IsPublished);
        if (query.Publication == StorefrontPublicationFilter.Hidden) source = source.Where(x => x.Publication == null || !x.Publication.IsPublished);
        if (query.Publication == StorefrontPublicationFilter.Unconfigured) source = source.Where(x => x.Publication == null);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) source = source.Where(x => EF.Functions.Collate(x.Product.Name, "Latin1_General_100_CI_AI").Contains(search) ||
            x.Publication != null && x.Publication.DisplayName != null && EF.Functions.Collate(x.Publication.DisplayName, "Latin1_General_100_CI_AI").Contains(search) ||
            db.ProductVariants.Any(v => v.StoreId == Store && v.ProductId == x.Product.Id && (v.Sku.Contains(search) || v.ProductVariantNameNormalized != null && v.ProductVariantNameNormalized.Contains(search))));
        var total = await source.CountAsync(ct);
        var rows = await source.OrderBy(x => x.Product.Name).ThenBy(x => x.Product.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(query, total, await CanManage(ct), rows.Select(x => new StorefrontProductListItem(x.Product.Id, x.Product.Name,
            x.Publication?.DisplayName, x.Publication?.DisplayGroup, x.Publication != null, x.Publication?.IsPublished ?? false,
            x.Product.IsActive && x.Product.IsSellable, Token(x.Publication?.RowVersion))).ToList());
    }

    public async Task<StorefrontProductPage> GetAsync(int productId, CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewProducts, ct);
        var source = await Source(productId, ct);
        var publication = await db.StorefrontProducts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ProductId == productId, ct);
        var request = publication is null ? new StorefrontProductRequest() : await Request(publication, ct);
        // Deleted source options disappear from the editor; saving resets their retained publication flags.
        request.Variants.RemoveAll(x => !source.Variants.Any(v => v.Id == x.VariantId));
        foreach (var variant in source.Variants)
        {
            var choice = request.Variants.SingleOrDefault(x => x.VariantId == variant.Id);
            if (choice is null) { choice = new() { VariantId = variant.Id }; request.Variants.Add(choice); }
            choice.Units.RemoveAll(x => !source.Conversions.Any(c => c.ProductVariantId == variant.Id && c.Id == x.ProductUnitConversionId && c.UnitId != source.Product.BaseUnitId));
            foreach (var conversion in source.Conversions.Where(x => x.ProductVariantId == variant.Id && x.UnitId != source.Product.BaseUnitId))
                if (!choice.Units.Any(x => x.ProductUnitConversionId == conversion.Id)) choice.Units.Add(new() { ProductUnitConversionId = conversion.Id });
        }
        var mediaIds = request.Media.Select(x => x.MediaAssetId).ToList();
        var assets = await db.MediaAssets.AsNoTracking().Where(x => x.StoreId == Store && mediaIds.Contains(x.Id)).ToListAsync(ct);
        var options = (await SearchMediaCore(null, 1, ct)).Concat(assets.Select(MediaOption)).DistinctBy(x => x.Id).ToList();
        return new(productId, source.Product.Name, source.Product.IsActive && source.Product.IsSellable,
            await db.StorefrontSettings.Where(x => x.StoreId == Store).Select(x => x.ShippingByWeight).SingleOrDefaultAsync(ct),
            await CanManage(ct), request, source.Variants.Select(v => new StorefrontVariantOption(v.Id, v.ProductVariantName ?? v.Sku, v.Sku,
                source.Product.BaseUnitId, source.BaseUnit?.Name ?? "Đơn vị gốc không còn hợp lệ",
                ProductSalePricePolicy.Pos(v, StorefrontCatalogQueries.BaseConversion(source.Product, source.Conversions.Where(x => x.ProductVariantId == v.Id)), wholesale: false),
                v.IsActive, source.Conversions.Where(x => x.ProductVariantId == v.Id && x.UnitId != source.Product.BaseUnitId).Select(c =>
                    new StorefrontUnitOption(c.Id, c.UnitId, c.Unit.Name, c.Factor, ProductSalePricePolicy.Pos(v, c, wholesale: false), c.IsActive && c.Unit.IsActive)).ToList())).ToList(), options);
    }

    private sealed record SourceData(Product Product, Unit? BaseUnit, List<ProductVariant> Variants, List<ProductUnitConversion> Conversions);
    private async Task<SourceData> Source(int productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == productId, ct)
            ?? throw new NotFoundAppException("Không tìm thấy sản phẩm tại cửa hàng này.");
        var baseUnit = await db.Units.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == product.BaseUnitId, ct);
        var variants = await db.ProductVariants.AsNoTracking().Where(x => x.StoreId == Store && x.ProductId == productId).OrderBy(x => x.Id).Take(201).ToListAsync(ct);
        Require(variants.Count <= 200, "Sản phẩm vượt giới hạn 200 biến thể của màn hình website.");
        var ids = variants.Select(x => x.Id).ToList();
        var conversions = await db.ProductUnitConversions.AsNoTracking().Include(x => x.Unit)
            .Where(x => x.StoreId == Store && ids.Contains(x.ProductVariantId) && x.Unit.StoreId == Store).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).Take(20001).ToListAsync(ct);
        Require(conversions.Count <= 20000 && conversions.GroupBy(x => x.ProductVariantId).All(x => x.Count() <= 100), "Mỗi biến thể hỗ trợ tối đa 100 đơn vị website.");
        foreach (var variant in variants) variant.Product = product;
        return new(product, baseUnit, variants, conversions);
    }

    private async Task<StorefrontProductRequest> Request(StorefrontProduct publication, CancellationToken ct)
    {
        var variants = await db.StorefrontVariants.AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontProductId == publication.Id && x.ProductId == publication.ProductId).ToListAsync(ct);
        var ids = variants.Select(x => x.Id).ToList();
        var sales = await db.StorefrontSellingUnits.AsNoTracking().Where(x => x.StoreId == Store && ids.Contains(x.StorefrontVariantId)).ToListAsync(ct);
        var media = await db.Set<StorefrontMedia>().AsNoTracking().Where(x => x.StoreId == Store && x.StorefrontProductId == publication.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        return new() { Version = Token(publication.RowVersion), IsPublished = publication.IsPublished, DisplayName = publication.DisplayName,
            Description = publication.Description, DisplayGroup = publication.DisplayGroup, SortOrder = publication.SortOrder,
            Variants = variants.Select(v => new StorefrontVariantRequest { VariantId = v.VariantId, IsPublished = v.IsPublished,
                BaseUnitIsPublished = v.BaseUnitIsPublished, BaseUnitWeightKg = v.BaseUnitWeightKg,
                Units = sales.Where(x => x.StorefrontVariantId == v.Id && x.VariantId == v.VariantId).Select(x => new StorefrontSellingUnitRequest {
                    ProductUnitConversionId = x.ProductUnitConversionId, IsPublished = x.IsPublished, WeightKg = x.WeightKg }).ToList() }).ToList(),
            Media = media.Select(x => new StorefrontMediaRequest { MediaAssetId = x.MediaAssetId, Kind = x.Kind, AltText = x.AltText, SortOrder = x.SortOrder }).ToList() };
    }

    public async Task SaveAsync(int productId, StorefrontProductRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Guard(StorefrontPermissions.ManageProducts, ct);
        ValidateShape(request);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await Lock(db, Store, ct);
            await LockMedia(db, ct);
            ClearPublicationTracking();
            var publication = await db.StorefrontProducts.SingleOrDefaultAsync(x => x.StoreId == Store && x.ProductId == productId, ct);
            Version(request.Version, publication?.RowVersion);
            var source = await Source(productId, ct);
            await ValidateChoices(source, request, ct);
            await ClaimMedia(request, ct);
            if (publication is null) { publication = new() { StoreId = Store, ProductId = productId }; db.StorefrontProducts.Add(publication); }
            publication.IsPublished = request.IsPublished; publication.DisplayName = Clean(request.DisplayName);
            publication.Description = Clean(request.Description); publication.DisplayGroup = Clean(request.DisplayGroup); publication.SortOrder = request.SortOrder;
            // Always advance the aggregate version, including changes confined to children.
            publication.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            var variants = await db.StorefrontVariants.Where(x => x.StoreId == Store && x.StorefrontProductId == publication.Id).ToListAsync(ct);
            var variantIds = variants.Select(x => x.Id).ToList();
            var sales = await db.StorefrontSellingUnits.Where(x => x.StoreId == Store && variantIds.Contains(x.StorefrontVariantId)).ToListAsync(ct);
            foreach (var variant in variants) { variant.IsPublished = false; variant.BaseUnitIsPublished = false; }
            foreach (var sale in sales) sale.IsPublished = false;
            foreach (var choice in request.Variants)
            {
                var variant = variants.SingleOrDefault(x => x.VariantId == choice.VariantId);
                if (variant is null) { variant = new() { StoreId = Store, StorefrontProductId = publication.Id, ProductId = productId, VariantId = choice.VariantId }; db.StorefrontVariants.Add(variant); variants.Add(variant); }
                variant.IsPublished = choice.IsPublished; variant.BaseUnitIsPublished = choice.BaseUnitIsPublished; variant.BaseUnitWeightKg = choice.BaseUnitWeightKg;
                await db.SaveChangesAsync(ct);
                foreach (var unit in choice.Units)
                {
                    var conversion = source.Conversions.Single(x => x.Id == unit.ProductUnitConversionId && x.ProductVariantId == choice.VariantId);
                    var sale = sales.SingleOrDefault(x => x.ProductUnitConversionId == conversion.Id);
                    if (sale is null) { sale = new() { StoreId = Store, StorefrontVariantId = variant.Id, VariantId = variant.VariantId, ProductUnitConversionId = conversion.Id, UnitId = conversion.UnitId }; db.StorefrontSellingUnits.Add(sale); sales.Add(sale); }
                    sale.IsPublished = unit.IsPublished; sale.WeightKg = unit.WeightKg;
                }
            }
            // Media links have no historical dependants. Replace them atomically; do not soft-delete Live-constrained foundation rows.
            await db.Set<StorefrontMedia>().Where(x => x.StoreId == Store && x.StorefrontProductId == publication.Id).ExecuteDeleteAsync(ct);
            foreach (var media in request.Media) db.Set<StorefrontMedia>().Add(new() { StoreId = Store, StorefrontProductId = publication.Id,
                MediaAssetId = media.MediaAssetId, Kind = media.Kind, AltText = (media.AltText ?? "").Trim(), SortOrder = media.SortOrder });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new ConflictAppException("Dữ liệu vừa thay đổi. Hãy tải lại trước khi lưu."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new ConflictAppException("Dữ liệu vừa được tạo hoặc thay đổi. Hãy tải lại trước khi lưu."); }
        finally { ClearPublicationTracking(); }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidateShape(StorefrontProductRequest request)
    {
        Validate(request);
        Require(request.Variants is not null && request.Variants.Count <= 200 && request.Variants.All(x => x is not null && x.VariantId > 0) &&
            request.Variants.Select(x => x.VariantId).Distinct().Count() == request.Variants.Count, "Danh sách biến thể không hợp lệ hoặc trùng mã.");
        Require(request.Media is not null && request.Media.Count <= 20 && request.Media.All(x => x is not null && x.MediaAssetId > 0 && Enum.IsDefined(x.Kind)) &&
            request.Media.Select(x => (x.MediaAssetId, x.Kind)).Distinct().Count() == request.Media.Count, "Tối đa 20 media, không trùng mã/loại.");
        foreach (var media in request.Media!) Validate(media);
        foreach (var variant in request.Variants!)
        {
            Kg(variant.BaseUnitWeightKg);
            Require(variant.Units is not null && variant.Units.Count <= 100 && variant.Units.All(x => x is not null && x.ProductUnitConversionId > 0) &&
                variant.Units.Select(x => x.ProductUnitConversionId).Distinct().Count() == variant.Units.Count, "Danh sách đơn vị không hợp lệ hoặc trùng mã.");
            foreach (var unit in variant.Units!) Kg(unit.WeightKg);
        }
    }

    private async Task ValidateChoices(SourceData source, StorefrontProductRequest request, CancellationToken ct)
    {
        var byWeight = await db.StorefrontSettings.AsNoTracking().Where(x => x.StoreId == Store).Select(x => x.ShippingByWeight).SingleOrDefaultAsync(ct);
        var any = false;
        foreach (var choice in request.Variants)
        {
            var variant = source.Variants.SingleOrDefault(x => x.Id == choice.VariantId);
            Require(variant is not null, "Biến thể không thuộc sản phẩm/cửa hàng này.");
            foreach (var unit in choice.Units)
            {
                var conversion = source.Conversions.SingleOrDefault(x => x.Id == unit.ProductUnitConversionId && x.ProductVariantId == choice.VariantId);
                Require(conversion is not null && conversion.UnitId != source.Product.BaseUnitId, "Đơn vị không thuộc biến thể/cửa hàng hoặc là đơn vị gốc. Dùng lựa chọn đơn vị gốc riêng.");
                if (!unit.IsPublished) continue;
                if (request.IsPublished && choice.IsPublished)
                {
                    Require(conversion!.IsActive && conversion.Unit.IsActive && conversion.Factor > 0, "Đơn vị/quy đổi phải đang hoạt động và có hệ số dương.");
                    Require(!byWeight || unit.WeightKg > 0, "Cần kg của mỗi đơn vị đang đăng khi tính phí theo kg.");
                }
                any |= choice.IsPublished;
            }
            if (choice.BaseUnitIsPublished)
            {
                Require(!request.IsPublished || !choice.IsPublished || !byWeight || choice.BaseUnitWeightKg > 0, "Cần kg đơn vị gốc đang đăng khi tính phí theo kg.");
                any |= choice.IsPublished;
            }
            if (request.IsPublished && choice.IsPublished) Require(source.Product.IsActive && source.Product.IsSellable && variant!.IsActive && source.BaseUnit is { IsActive: true },
                "Sản phẩm, biến thể và đơn vị gốc phải đang hoạt động/được bán.");
        }
        Require(!request.IsPublished || any, "Cần chọn ít nhất một biến thể và đơn vị hợp lệ trước khi đăng sản phẩm.");
        await ValidateMedia(request, ct);
    }

    private async Task ValidateMedia(StorefrontProductRequest request, CancellationToken ct)
    {
        var ids = request.Media.Select(x => x.MediaAssetId).Distinct().ToList();
        // Always read fresh after the global cleanup lock; no stale tracked asset may be resurrected.
        var assets = await db.MediaAssets.AsNoTracking().Where(x => x.StoreId == Store && ids.Contains(x.Id)).ToListAsync(ct);
        foreach (var media in request.Media) Require(assets.Any(x => x.Id == media.MediaAssetId && StorefrontCatalogQueries.MediaPath(x, media.Kind, allowCleanupDeadline: true) is not null),
            "Media phải là tài sản đã lưu của cửa hàng, đúng loại, không tạm và có đường dẫn uploads hợp lệ.");
    }

    private async Task ClaimMedia(StorefrontProductRequest request, CancellationToken ct)
    {
        var ids = request.Media.Select(x => x.MediaAssetId).Distinct().ToList();
        if (ids.Count == 0) return;
        // Deadline reset and storefront reference write share the transaction and cleanup lock.
        await db.MediaAssets.Where(x => x.StoreId == Store && ids.Contains(x.Id) && !x.IsTemp &&
            (x.TempToken == null || x.TempToken == "") && x.ExpireAtUtc != null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpireAtUtc, (DateTime?)null), ct);
    }

    public async Task<StorefrontPublicationResult> SetPublicationAsync(StorefrontPublicationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Guard(StorefrontPermissions.ManageProducts, ct);
        return await SetOne(request, ct);
    }

    public async Task<IReadOnlyList<StorefrontPublicationResult>> SetPublicationBulkAsync(StorefrontBulkPublicationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Guard(StorefrontPermissions.ManageProducts, ct);
        Require(request.Items is not null && request.Items.Count is > 0 and <= 100 && request.Items.All(x => x is not null && x.ProductId > 0) &&
            request.Items.Select(x => x.ProductId).Distinct().Count() == request.Items.Count, "Chọn từ 1 đến 100 sản phẩm, không trùng mã.");
        var result = new List<StorefrontPublicationResult>();
        foreach (var item in request.Items!) result.Add(await SetOne(item, ct));
        return result;
    }

    private async Task<StorefrontPublicationResult> SetOne(StorefrontPublicationRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await Lock(db, Store, ct);
            ClearPublicationTracking();
            var source = await db.Products.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == request.ProductId, ct);
            if (!source) return new(request.ProductId, StorefrontPublicationOutcome.NotFound, "Không tìm thấy sản phẩm tại cửa hàng này.", null);
            var publication = await db.StorefrontProducts.SingleOrDefaultAsync(x => x.StoreId == Store && x.ProductId == request.ProductId, ct);
            if (!WellFormedVersion(request.Version, publication is not null))
                return new(request.ProductId, StorefrontPublicationOutcome.Conflict, "Phiên bản không hợp lệ. Hãy tải lại sản phẩm.", null);
            // Explicit target state makes a lost-response retry harmless; never toggle blindly.
            if ((publication?.IsPublished ?? false) == request.IsPublished)
            {
                if (request.IsPublished && publication is not null)
                {
                    await LockMedia(db, ct);
                    var existing = await Request(publication, ct);
                    ValidateShape(existing); await ValidateMedia(existing, ct); await ClaimMedia(existing, ct);
                    await tx.CommitAsync(ct);
                }
                return new(request.ProductId, StorefrontPublicationOutcome.Unchanged, "Trạng thái đã đúng yêu cầu.", Token(publication?.RowVersion));
            }
            Version(request.Version, publication?.RowVersion);
            Require(publication is not null, "Hãy cấu hình biến thể và đơn vị trước khi đăng sản phẩm.");
            if (request.IsPublished)
            {
                await LockMedia(db, ct);
                var choices = await Request(publication!, ct); choices.IsPublished = true;
                ValidateShape(choices); await ValidateChoices(await Source(request.ProductId, ct), choices, ct);
                await ClaimMedia(choices, ct);
            }
            publication!.IsPublished = request.IsPublished;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return new(request.ProductId, StorefrontPublicationOutcome.Updated, request.IsPublished ? "Đã đăng website." : "Đã ẩn website.", Token(publication.RowVersion));
        }
        catch (ValidationAppException ex) { return new(request.ProductId, StorefrontPublicationOutcome.Invalid, ex.Message, null); }
        catch (ConflictAppException ex) { return new(request.ProductId, StorefrontPublicationOutcome.Conflict, ex.Message, null); }
        catch (DbUpdateConcurrencyException) { return new(request.ProductId, StorefrontPublicationOutcome.Conflict, "Dữ liệu vừa thay đổi. Hãy tải lại.", null); }
        finally { ClearPublicationTracking(); }
    }

    private void ClearPublicationTracking()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(x => x.Entity is StorefrontProduct or StorefrontVariant or StorefrontSellingUnit or StorefrontMedia).ToList())
            entry.State = EntityState.Detached;
    }

    public async Task<IReadOnlyList<StorefrontMediaOption>> SearchMediaAsync(string? search, int page = 1, CancellationToken ct = default)
    {
        await Guard(StorefrontPermissions.ViewProducts, ct);
        return await SearchMediaCore(search, page, ct);
    }
    private async Task<IReadOnlyList<StorefrontMediaOption>> SearchMediaCore(string? search, int page, CancellationToken ct)
    {
        Page(page, 50); Require((search?.Length ?? 0) <= 200, "Từ khóa media quá dài.");
        var query = db.MediaAssets.AsNoTracking().Where(x => x.StoreId == Store && !x.IsTemp && (x.TempToken == null || x.TempToken == ""));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.OriginalFileName != null && x.OriginalFileName.Contains(search.Trim()) || x.StoragePath.Contains(search.Trim()));
        var assets = await query.OrderByDescending(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync(ct);
        return assets.Where(x => StorefrontCatalogQueries.MediaPath(x, StorefrontMediaKind.Image, allowCleanupDeadline: true) is not null || StorefrontCatalogQueries.MediaPath(x, StorefrontMediaKind.Model3D, allowCleanupDeadline: true) is not null).Select(MediaOption).ToList();
    }
    private static StorefrontMediaOption MediaOption(MediaAsset asset) => new(asset.Id, asset.StoragePath, asset.OriginalFileName ?? Path.GetFileName(asset.StoragePath), asset.ContentType);
}
