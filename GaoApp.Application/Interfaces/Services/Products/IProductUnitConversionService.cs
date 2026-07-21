using GaoApp.Application.DTOs.Products;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.Products;

/// <summary>
/// Service quản lý ProductUnitConversion và barcode theo đơn vị.
/// </summary>
public interface IProductUnitConversionService
{
    Task<List<UpsertProductUnitConversionRequest>> GetByVariantIdAsync(
        int productVariantId,
        CancellationToken ct = default);

    Task<int> SaveConversionAsync(
        ProductUnitConversionUpsertDto dto,
        CancellationToken ct = default);

    Task<int> SaveBarcodeAsync(
        UpsertProductVariantUnitBarcodeRequest dto,
        CancellationToken ct = default);

    Task<ProductUnitConversion> CreateAsync(
        CreateProductUnitConversionRequest request,
        CancellationToken ct = default);

    Task<List<ProductVariantBarcodeHistoryRowDto>> GetBarcodeHistoryByConversionIdAsync(
        int productUnitConversionId,
        int take = 20,
        CancellationToken ct = default);
}