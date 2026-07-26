using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Products;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Products;

/// <summary>
/// Service trung gian cho màn hình quản lý ProductVariant.
///
/// CHỐT KIẾN TRÚC:
/// - Frontend bảng _Variants cần mỗi dòng có:
///   + SKU
///   + ProductVariantName
///   + CostPrice
///   + Price
///   + IsActive
///   + PrimaryProductImageId
///   + AttributeValueIds
///   + IsLocked
/// - Vì vậy service phải map đầy đủ dữ liệu từ entity -> ProductVariantRowDto
/// - Save vẫn đẩy toàn bộ list variant xuống repository xử lý.
/// </summary>
public sealed class ProductVariantService : IProductVariantService
{
    private readonly IProductVariantRepository _repo;

    public ProductVariantService(IProductVariantRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Lấy danh sách thuộc tính + giá trị thuộc tính
    /// để dựng UI chọn combo tạo biến thể.
    /// </summary>
    public Task<List<AttributeWithValuesDto>> GetAttributesAsync(int storeId, CancellationToken ct = default)
        => _repo.GetAttributesWithValuesAsync(storeId, ct);

    /// <summary>
    /// Lấy danh sách variant của sản phẩm cho màn hình edit/create.
    /// </summary>
    public async Task<List<ProductVariantRowDto>> GetVariantsAsync(
        int storeId,
        int productId,
        CancellationToken ct = default)
    {
        var list = await _repo.GetByProductAsync(storeId, productId, ct);

        return list.Select(v =>
        {
            // NEW:
            // Giá bán hiển thị ngoài bảng Variant không lấy từ ProductVariant.Price nữa.
            // Vì mỗi variant luôn có 1 ProductUnitConversion gốc được tạo tự động.
            var baseConversion = v.UnitConversions?
                .Where(c => !c.IsDeleted)
                .OrderByDescending(c => c.IsBaseUnit)
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.Id)
                .FirstOrDefault();

            return new ProductVariantRowDto
            {
                Id = v.Id,
                Sku = v.Sku,
                ProductVariantName = v.ProductVariantName,
                CostPrice = v.CostPrice,

                // Giữ Price cũ để tránh vỡ DTO cũ, nhưng UI mới không dùng để nhập giá nữa.
                Price = v.Price,

                // NEW: hiển thị giá của đơn vị gốc
                BaseUnitPrice = baseConversion?.Price,
                BaseUnitWholesalePrice = baseConversion?.WholesalePrice,
                BaseUnitName = baseConversion?.Unit?.Name,

                IsActive = v.IsActive,
                HasInputInvoice = v.HasInputInvoice,
                PrimaryProductImageId = v.PrimaryProductImageId,
                AttributeValueIds = v.AttributeValues
                    .Where(x => !x.IsDeleted)
                    .Select(x => x.AttributeValueId)
                    .Distinct()
                    .ToList(),
                IsLocked = false
            };
        }).ToList();
    }

    /// <summary>
    /// Lưu toàn bộ danh sách variant.
    /// Repository sẽ xử lý:
    /// - create
    /// - update
    /// - restore soft delete
    /// - sync attribute mappings
    /// - tự sinh / lưu ProductVariantNameNormalized
    /// </summary>
    public async Task<Result> SaveVariantsAsync(
        int storeId,
        int productId,
        List<ProductVariantRowDto> variants,
        int? userId,
        CancellationToken ct)
    {
        try
        {
            await _repo.SaveVariantsAsync(storeId, productId, variants, userId, ct);
            return Result.Success();
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(
                Error.Validation(
                    "ProductVariant.BusinessRule",
                    ex.SafeMessage));
        }
    }

    /// <summary>
    /// Đổi trạng thái mở bán / ngưng bán của 1 variant.
    /// </summary>
    public async Task<Result> ToggleStatusAsync(
        int storeId,
        int variantId,
        int? userId,
        CancellationToken ct)
    {
        try
        {
            var ok = await _repo.ToggleStatusAsync(storeId, variantId, userId, ct);
            return ok
                ? Result.Success()
                : Result.Failure(Error.NotFound("Không thể đổi trạng thái biến thể."));
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(
                Error.Validation(
                    "ProductVariant.BusinessRule",
                    ex.SafeMessage));
        }
    }

    /// <summary>
    /// Soft delete variant.
    /// Nếu variant đã phát sinh giao dịch thì repo sẽ chặn.
    /// </summary>
    public async Task<Result> SoftDeleteVariantAsync(
        int storeId,
        int variantId,
        int? userId,
        CancellationToken ct)
    {
        try
        {
            var ok = await _repo.SoftDeleteVariantAsync(storeId, variantId, userId, ct);
            return ok
                ? Result.Success()
                : Result.Failure(Error.NotFound("Xóa biến thể thất bại."));
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(
                Error.Validation(
                    "ProductVariant.BusinessRule",
                    ex.SafeMessage));
        }
    }

    /// <summary>
    /// Gán / đổi / bỏ ảnh đại diện của variant.
    /// </summary>
    public async Task<Result> SetVariantImageAsync(
        int storeId,
        int variantId,
        int? primaryProductImageId,
        int? userId,
        CancellationToken ct)
    {
        try
        {
            var ok = await _repo.SetVariantImageAsync(
                storeId,
                variantId,
                primaryProductImageId,
                userId,
                ct);

            return ok
                ? Result.Success()
                : Result.Failure(Error.NotFound("Không lưu được ảnh biến thể."));
        }
        catch (BusinessRuleException ex)
        {
            return Result.Failure(
                Error.Validation(
                    "ProductVariant.ImageBusinessRule",
                    ex.SafeMessage));
        }
    }
}
