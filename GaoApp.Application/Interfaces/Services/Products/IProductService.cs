using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Interfaces.Services.Products;

public interface IProductService
{
    Task<PagedResult<ProductListItemDto>> SearchCatalogAsync(int storeId, string? search, int? categoryId,
        bool? isActive, bool? isSellable, int page, int pageSize, ProductListFilters filters, CancellationToken ct = default);
    Task<List<ProductFilterOptionDto>> FilterOptionsAsync(int storeId, string kind, string? term, int? selectedId, CancellationToken ct = default);


    Task<PagedResult<ProductListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<PagedResult<ProductListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int? categoryId,
        bool? isActive,
        bool? isSellable,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int PosAllowedItems, int NotForPosItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        int storeId,
        ProductCreateDto dto,
        int? userId,
        CancellationToken ct = default);

    Task<ProductEditDto?> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        int storeId,
        UpdateProductRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<string> GetUniqueAliasAsync(
        int storeId,
        string nameOrAlias,
        int? excludeId,
        CancellationToken ct = default);

    Task<Result> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<ProductDetailDto?> GetDetailGalleryAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<List<ProductImageListItemDto>> GetImagesForVariantAsync(
        int storeId,
        int productId,
        CancellationToken ct = default);
}
