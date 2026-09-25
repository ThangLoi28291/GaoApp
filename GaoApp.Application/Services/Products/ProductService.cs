using GaoApp.Application.Common;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Media;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;


using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Products;

public sealed class ProductService : IProductService
{
    private readonly IProductRepository _repo;
    private readonly IProductImageService _productImageService;
    private readonly IProductVariantRepository _variantRepo;
    private readonly IAppUnitOfWork _uow;

    public ProductService(
        IProductRepository repo,
        IProductImageService productImageService,
        IProductVariantRepository variantRepo,
        IAppUnitOfWork uow)
    {
        _repo = repo;
        _productImageService = productImageService;
        _variantRepo = variantRepo;
        _uow = uow;
    }

    public Task<PagedResult<ProductListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
        => _repo.GetPagedAsync(storeId, search, page, pageSize, ct);

    public Task<PagedResult<ProductListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int? categoryId,
        bool? isActive,
        bool? isSellable,
        int page,
        int pageSize,
        CancellationToken ct = default)
        => _repo.GetPagedAsync(
            storeId,
            search,
            categoryId,
            isActive,
            isSellable,
            page,
            pageSize,
            ct);

    public Task<(int TotalItems, int PosAllowedItems, int NotForPosItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default)
        => _repo.GetSummaryAsync(storeId, ct);

    /// <summary>
    /// Sinh barcode nội bộ khả dụng cho variant.
    ///
    /// Rule:
    /// - ưu tiên prefix 20
    /// - nếu trùng thì thử 21, 22, 23... đến 29
    /// - check cả dữ liệu soft delete để tránh vỡ unique index DB
    /// </summary>
    private async Task<string> GenerateAvailableInternalBarcodeAsync(
        int storeId,
        int variantId,
        CancellationToken ct)
    {
        var prefixes = new[]
        {
            "20", "21", "22", "23", "24",
            "25", "26", "27", "28", "29"
        };

        foreach (var prefix in prefixes)
        {
            var candidate = Ean13Helper.GenerateInternal(storeId, variantId, prefix);

            var exists = await _repo.ExistsVariantUnitBarcodeAsync(
                storeId,
                candidate,
                ct);

            if (!exists)
                return candidate;
        }

        throw new BusinessRuleException(
            "Không thể cấp barcode nội bộ cho sản phẩm. Vui lòng thử lại hoặc liên hệ quản lý.");
    }

    public async Task<Result<int>> CreateAsync(
       int storeId, ProductCreateDto dto, int? userId, CancellationToken ct = default)
    {
        await using var transaction = await _uow.BeginTransactionAsync(ct);
        var result = await CreateCoreAsync(storeId, dto, userId, ct);
        if (result.IsFailure) await transaction.RollbackAsync(ct);
        else { _uow.EnsureCanCommit(); await transaction.CommitAsync(ct); }
        return result;
    }

    private async Task<Result<int>> CreateCoreAsync(
       int storeId,
       ProductCreateDto dto,
       int? userId,
       CancellationToken ct = default)
    {
        try
        {
            var normalizedName = dto.Name?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return Result.ValidationFailure<int>(
                    new[]
                    {
                    new ValidationError(
                        nameof(dto.Name),
                        "Vui lòng nhập tên sản phẩm.")
                    });
            }

            // Create cho phép bỏ trống Alias.
            // Khi đó sinh Alias từ Name như nghiệp vụ hiện tại.
            var aliasInput = dto.Alias?.Trim();

            if (string.IsNullOrWhiteSpace(aliasInput))
            {
                aliasInput = normalizedName;
            }

            var alias = await GetUniqueAliasAsync(
                storeId,
                aliasInput,
                excludeId: null,
                ct);

            var entity = new Product
            {
                StoreId = storeId,
                Name = normalizedName,
                Alias = alias,
                CategoryId = dto.CategoryId!.Value,
                SupplierId = dto.SupplierId!.Value,
                BrandId = dto.BrandId,
                TaxId = dto.TaxId,
                BaseUnitId = dto.BaseUnitId!.Value,
                BasePrice = dto.BasePrice,
                Description = dto.Description?.Trim(),
                Content = dto.Content,
                IsActive = true,
                IsSellable = dto.IsSellable
            };

            // =========================================================
            // 1) Tạo Product
            // =========================================================
            await _repo.AddAsync(entity, ct);
            await _repo.SaveChangesAsync(ct);

            // =========================================================
            // 2) Tạo Variant mặc định
            // =========================================================
            var defaultVariantName = entity.Name.Trim();

            var defaultVariant = new ProductVariant
            {
                StoreId = storeId,
                ProductId = entity.Id,
                Sku = entity.Alias,
                ProductVariantName = defaultVariantName,
                ProductVariantNameNormalized =
                    ProductVariantNameHelper.NormalizeForSearch(
                        defaultVariantName),
                CostPrice = 0m,
                Price = null,
                IsActive = true,
                PrimaryProductImageId = null
            };

            await _variantRepo.AddAsync(defaultVariant, ct);
            await _variantRepo.SaveChangesAsync(ct);

            // =========================================================
            // 3) Tạo ProductUnitConversion base unit
            // =========================================================
            var baseConversion = new ProductUnitConversion
            {
                StoreId = storeId,
                ProductVariantId = defaultVariant.Id,
                UnitId = entity.BaseUnitId,
                Factor = 1m,
                IsBaseUnit = true,
                IsDefaultForSale = true,
                Price = null,
                IsActive = true,
                SortOrder = 0
            };

            await _repo.AddProductUnitConversionAsync(
                baseConversion,
                ct);

            await _repo.SaveChangesAsync(ct);

            // =========================================================
            // 4) Tạo barcode nội bộ cho base unit
            // =========================================================
            var internalBarcode =
                await GenerateAvailableInternalBarcodeAsync(
                    storeId,
                    defaultVariant.Id,
                    ct);

            var defaultUnitBarcode =
                new ProductVariantUnitBarcode
                {
                    StoreId = storeId,
                    ProductUnitConversionId = baseConversion.Id,
                    Barcode = internalBarcode,
                    BarcodeType = BarcodeType.Internal,
                    IsPrimary = true,
                    IsActive = true,
                    Note = "Tự sinh khi tạo sản phẩm mặc định"
                };

            await _repo.AddProductVariantUnitBarcodeAsync(
                defaultUnitBarcode,
                ct);

            await _repo.SaveChangesAsync(ct);

            // =========================================================
            // 5) Commit ảnh temp
            // =========================================================
            await _productImageService.CommitTempImagesAsync(
                storeId,
                entity.Id,
                dto.TempImageTokens,
                dto.PrimaryTempToken,
                userId,
                ct);

            // =========================================================
            // 6) Save cuối
            // =========================================================
            await _repo.SaveChangesAsync(ct);

            return Result<int>.Success(entity.Id);
        }
        catch (BusinessRuleException ex)
        {
            return Result<int>.Failure(
                Error.Validation(
                    "Product.BusinessRule",
                    ex.SafeMessage));
        }
    }

    public async Task<ProductEditDto?> GetForEditAsync(int storeId, int id, CancellationToken ct = default)
    {
        var entity = await _repo.GetDetailAsync(storeId, id, ct);
        if (entity == null) return null;

        return new ProductEditDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Alias = entity.Alias,
            CategoryId = entity.CategoryId,
            SupplierId = entity.SupplierId,
            BrandId = entity.BrandId,
            TaxId = entity.TaxId,
            BaseUnitId = entity.BaseUnitId,
            BasePrice = entity.BasePrice,
            Description = entity.Description,
            Content = entity.Content,
            IsSellable = entity.IsSellable,
            RowVersion = entity.RowVersion ?? Array.Empty<byte>(),
            Images = entity.ProductImages
                .OrderBy(x => x.SortOrder)
                .Select(x => new ProductImageEditItemDto
                {
                    ProductImageId = x.Id,
                    Url = "/" + x.MediaAsset.StoragePath,
                    IsPrimary = x.IsPrimary,
                    SortOrder = x.SortOrder
                })
                .ToList()
        };
    }

    public async Task<Result> UpdateAsync(
       int storeId, UpdateProductRequest dto, int? userId, CancellationToken ct = default)
    {
        await using var transaction = await _uow.BeginTransactionAsync(ct);
        var result = await UpdateCoreAsync(storeId, dto, userId, ct);
        if (result.IsFailure) await transaction.RollbackAsync(ct);
        else { _uow.EnsureCanCommit(); await transaction.CommitAsync(ct); }
        return result;
    }

    private async Task<Result> UpdateCoreAsync(
       int storeId,
       UpdateProductRequest dto,
       int? userId,
       CancellationToken ct = default)
    {
        try
        {
            var entity = await _repo.GetDetailAsync(
                storeId,
                dto.Id,
                ct);

            if (entity == null)
            {
                return Result.Failure(
                    Error.NotFound(
                        "Không tìm thấy sản phẩm."));
            }

            var normalizedName = dto.Name?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return Result.ValidationFailure(
                    new[]
                    {
                    new ValidationError(
                        nameof(dto.Name),
                        "Vui lòng nhập tên sản phẩm.")
                    });
            }

            var normalizedAlias = dto.Alias?.Trim();

            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                return Result.ValidationFailure(
                    new[]
                    {
                    new ValidationError(
                        nameof(dto.Alias),
                        "Alias không hợp lệ.")
                    });
            }

            // =========================================================
            // A) Update product fields
            // =========================================================
            entity.Name = normalizedName;

            entity.Alias = await GetUniqueAliasAsync(
                storeId,
                normalizedAlias,
                dto.Id,
                ct);

            entity.CategoryId = dto.CategoryId!.Value;
            entity.SupplierId = dto.SupplierId!.Value;
            entity.BrandId = dto.BrandId;
            entity.TaxId = dto.TaxId;
            entity.BaseUnitId = dto.BaseUnitId!.Value;
            entity.BasePrice = dto.BasePrice;
            entity.Description = dto.Description?.Trim();
            entity.Content = dto.Content;

            if (dto.IsSellable.HasValue)
            {
                entity.IsSellable = dto.IsSellable.Value;
            }

            // =========================================================
            // B) Sync images
            // =========================================================
            var orderedItems =
                ParseImageState(dto.ImagesStateJson);

            await _productImageService.SyncEditAsync(
                storeId,
                dto.Id,
                orderedItems,
                dto.PrimaryKey,
                userId,
                ct);

            await _repo.SaveChangesAsync(ct);

            // =========================================================
            // C) Clear tracking ProductImages
            // =========================================================
            await _productImageService.ClearImageTrackingAsync(
                storeId,
                dto.Id);

            // =========================================================
            // D) Set primary image atomic
            // =========================================================
            await _productImageService.SetPrimaryAsync(
                storeId,
                dto.Id,
                dto.PrimaryKey,
                ct);

            return Result.Success();
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(
                Error.Validation(
                    "Product.BusinessRule",
                    ex.SafeMessage));
        }
    }

    public async Task<Result> ToggleStatusAsync(int storeId, int id, int? userId, CancellationToken ct = default)
    {
        var ok = await _repo.ToggleStatusAsync(storeId, id, userId, ct);
        return ok
            ? Result.Success()
            : Result.Failure(Error.NotFound("Không thể đổi trạng thái sản phẩm."));
    }

    public async Task<Result> SoftDeleteAsync(int storeId, int id, int? userId, CancellationToken ct = default)
    {
        try
        {
            var ok = await _repo.SoftDeleteAsync(storeId, id, userId, ct);
            return ok
                ? Result.Success()
                : Result.Failure(Error.NotFound("Xóa sản phẩm thất bại."));
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(
                Error.Validation(
                    "Product.BusinessRule",
                    ex.SafeMessage));
        }
    }

    public async Task<string> GetUniqueAliasAsync(
        int storeId,
        string nameOrAlias,
        int? excludeId,
        CancellationToken ct = default)
    {
        var baseAlias = SlugifyVN(nameOrAlias);
        if (string.IsNullOrWhiteSpace(baseAlias))
            baseAlias = "sp";

        var alias = baseAlias;
        var i = 0;

        while (await _repo.ExistsAliasAsync(storeId, alias, excludeId, ct))
        {
            i++;
            alias = $"{baseAlias}-{i}";
            if (i > 5000) break;
        }

        return alias;
    }

    private static List<ProductImageStateDto> ParseImageState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<ProductImageStateDto>();

        try
        {
            return JsonSerializer.Deserialize<List<ProductImageStateDto>>(json)
                   ?? throw new BusinessRuleException(
                       "Dữ liệu trạng thái ảnh không hợp lệ.");
        }
        catch (JsonException ex)
        {
            throw new BusinessRuleException(
                "Dữ liệu trạng thái ảnh không hợp lệ.",
                ex);
        }
    }

    private static string SlugifyVN(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var s = input.Trim().ToLowerInvariant().Replace("đ", "d");
        var normalized = s.Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        s = sb.ToString().Normalize(NormalizationForm.FormC);
        s = Regex.Replace(s, @"[^a-z0-9]+", "-");
        s = s.Trim('-');
        s = Regex.Replace(s, @"-+", "-");
        return s;
    }

    public async Task<ProductDetailDto?> GetDetailGalleryAsync(int storeId, int id, CancellationToken ct = default)
    {
        var p = await _repo.GetDetailAsync(storeId, id, ct);
        if (p == null) return null;

        var imgs = p.ProductImages
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.SortOrder)
            .Select(x => new ProductImageItemDto
            {
                Id = x.Id,
                Url = "/" + x.MediaAsset.StoragePath,
                IsPrimary = x.IsPrimary,
                SortOrder = x.SortOrder
            })
            .ToList();

        return new ProductDetailDto
        {
            Id = p.Id,
            Name = p.Name,
            Alias = p.Alias,
           
            Images = imgs
        };
    }

    public async Task<List<ProductImageListItemDto>> GetImagesForVariantAsync(
        int storeId,
        int productId,
        CancellationToken ct = default)
    {
        var p = await _repo.GetDetailAsync(storeId, productId, ct);
        if (p == null) return new();

        return p.ProductImages
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.SortOrder)
            .Select(x => new ProductImageListItemDto
            {
                Id = x.Id,
                Url = "/" + x.MediaAsset.StoragePath,
                IsPrimary = x.IsPrimary
                
            })
            .ToList();
    }
}
