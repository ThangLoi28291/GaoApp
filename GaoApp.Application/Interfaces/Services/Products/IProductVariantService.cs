using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Interfaces.Services.Products;

public interface IProductVariantService
{
    Task<List<AttributeWithValuesDto>> GetAttributesAsync(int storeId, CancellationToken ct = default);

    Task<List<ProductVariantRowDto>> GetVariantsAsync(int storeId, int productId, CancellationToken ct = default);

    Task<Result> SaveVariantsAsync(
        int storeId,
        int productId,
        List<ProductVariantRowDto> variants,
        int? userId,
        CancellationToken ct = default);

    Task<Result> ToggleStatusAsync(
        int storeId,
        int variantId,
        int? userId,
        CancellationToken ct);

    Task<Result> SoftDeleteVariantAsync(
        int storeId,
        int variantId,
        int? userId,
        CancellationToken ct);

    Task<Result> SetVariantImageAsync(
        int storeId,
        int variantId,
        int? primaryProductImageId,
        int? userId,
        CancellationToken ct);
}