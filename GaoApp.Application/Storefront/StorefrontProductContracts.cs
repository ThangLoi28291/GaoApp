using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Storefront;

namespace GaoApp.Application.Storefront;

public enum StorefrontPublicationFilter { All, Published, Hidden, Unconfigured }
public sealed class StorefrontProductQuery
{
    [StringLength(200)] public string? Search { get; set; }
    public StorefrontPublicationFilter Publication { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
public sealed class StorefrontProductRequest
{
    public string Version { get; set; } = "";
    public bool IsPublished { get; set; }
    [StringLength(200)] public string? DisplayName { get; set; }
    // Plain text; render with normal Razor encoding, never Html.Raw.
    [StringLength(6000)] public string? Description { get; set; }
    [StringLength(100)] public string? DisplayGroup { get; set; }
    public int SortOrder { get; set; }
    public List<StorefrontVariantRequest> Variants { get; set; } = [];
    public List<StorefrontMediaRequest> Media { get; set; } = [];
}
public sealed class StorefrontVariantRequest
{
    public int VariantId { get; set; }
    // Variant gate, base unit and conversions are separate explicit opt-ins (pack-only is supported).
    public bool IsPublished { get; set; }
    public bool BaseUnitIsPublished { get; set; }
    public decimal? BaseUnitWeightKg { get; set; }
    public List<StorefrontSellingUnitRequest> Units { get; set; } = [];
}
public sealed class StorefrontSellingUnitRequest
{
    public int ProductUnitConversionId { get; set; }
    public bool IsPublished { get; set; }
    public decimal? WeightKg { get; set; }
}
public sealed class StorefrontMediaRequest
{
    public int MediaAssetId { get; set; }
    public StorefrontMediaKind Kind { get; set; }
    [StringLength(300)] public string AltText { get; set; } = "";
    public int SortOrder { get; set; }
}
public sealed class StorefrontPublicationRequest
{
    public int ProductId { get; set; }
    public string Version { get; set; } = "";
    public bool IsPublished { get; set; }
}
public sealed class StorefrontBulkPublicationRequest
{
    public List<StorefrontPublicationRequest> Items { get; set; } = [];
}
public enum StorefrontPublicationOutcome { Updated, Unchanged, Invalid, Conflict, NotFound }
public sealed record StorefrontPublicationResult(int ProductId, StorefrontPublicationOutcome Outcome, string Message, string? Version);
public sealed record StorefrontProductListItem(int ProductId, string Name, string? DisplayName, string? DisplayGroup,
    bool IsConfigured, bool IsPublished, bool IsSellable, string Version);
public sealed record StorefrontProductListPage(StorefrontProductQuery Query, int TotalCount, bool CanManage,
    IReadOnlyList<StorefrontProductListItem> Items);
public sealed record StorefrontUnitOption(int ConversionId, int UnitId, string Name, decimal Factor, decimal RetailPrice, bool IsActive);
public sealed record StorefrontVariantOption(int VariantId, string Name, string Sku, int BaseUnitId, string BaseUnitName,
    decimal BaseRetailPrice, bool IsActive, IReadOnlyList<StorefrontUnitOption> Units);
public sealed record StorefrontMediaOption(int Id, string Path, string Name, string? ContentType);
public sealed record StorefrontProductPage(int ProductId, string Name, bool IsSellable, bool ShippingByWeight, bool CanManage,
    StorefrontProductRequest Product, IReadOnlyList<StorefrontVariantOption> VariantOptions,
    IReadOnlyList<StorefrontMediaOption> MediaOptions);

public interface IStorefrontProductService
{
    Task<StorefrontProductListPage> ListAsync(StorefrontProductQuery query, CancellationToken ct = default);
    Task<StorefrontProductPage> GetAsync(int productId, CancellationToken ct = default);
    Task SaveAsync(int productId, StorefrontProductRequest request, CancellationToken ct = default);
    Task<StorefrontPublicationResult> SetPublicationAsync(StorefrontPublicationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StorefrontPublicationResult>> SetPublicationBulkAsync(StorefrontBulkPublicationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StorefrontMediaOption>> SearchMediaAsync(string? search, int page = 1, CancellationToken ct = default);
}

public enum StorefrontCatalogSort { Featured, NameAsc, NameDesc }
public sealed class StorefrontCatalogQuery
{
    public string? Search { get; set; }
    public string? DisplayGroup { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 24;
    public StorefrontCatalogSort Sort { get; set; }
}
public sealed record StorefrontCatalogUnit(int VariantId, int UnitId, int? ProductUnitConversionId, string Name,
    decimal BaseMultiplier, decimal RetailPrice, decimal? WeightKg);
public sealed record StorefrontCatalogVariant(int VariantId, string Name, IReadOnlyList<StorefrontCatalogUnit> Units);
public sealed record StorefrontCatalogMedia(int MediaAssetId, StorefrontMediaKind Kind, string Path, string AltText);
public sealed record StorefrontCatalogProduct(int ProductId, string Name, string? Description, string? DisplayGroup, int SortOrder,
    IReadOnlyList<StorefrontCatalogVariant> Variants, IReadOnlyList<StorefrontCatalogMedia> Media);
public sealed record StorefrontCatalogPage(int TotalCount, IReadOnlyList<StorefrontCatalogProduct> Items);
// Server-side source for the tenant-authorized media stream; never serialize StoragePath to customers.
public sealed record StorefrontCatalogMediaSource(int MediaAssetId, StorefrontMediaKind Kind, string StoragePath, string? ContentType, long SizeBytes);
// All methods re-read publication, source status, prices and kg. No customer supplied StoreId or price.
// WEB04 must call ResolveAsync again inside its placement transaction while holding the store configuration lock
// (GaoApp:Storefront:Configuration:{StoreId}) to serialize publication/settings changes. Old order snapshots do not use this API.
// Media Path is a logical /shop/media/{id} URL; WEB03 must authorize it through current catalog visibility.
// No raw uploads directory is shared by the public host. 3D viewer/resource validation belongs to WEB08.
public interface IStorefrontCatalogService
{
    Task<StorefrontCatalogPage> SearchAsync(StorefrontCatalogQuery query, CancellationToken ct = default);
    Task<StorefrontCatalogProduct?> GetAsync(int productId, CancellationToken ct = default);
    Task<StorefrontCatalogUnit?> ResolveAsync(int variantId, int unitId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetGroupsAsync(CancellationToken ct = default);
    Task<StorefrontCatalogMediaSource?> GetMediaAsync(int mediaAssetId, CancellationToken ct = default);
}
