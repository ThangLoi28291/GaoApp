using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Interfaces.Services.Products;

public interface IProductUnitBarcodeReadService
{
    Task<List<ProductVariantUnitBarcodeDto>> GetByConversionIdAsync(int conversionId, CancellationToken ct = default);
}