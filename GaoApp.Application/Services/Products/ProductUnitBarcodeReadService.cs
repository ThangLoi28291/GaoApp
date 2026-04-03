using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Products;

namespace GaoApp.Application.Services.Products;

/// <summary>
/// Service đọc danh sách barcode thuộc về một conversion để hiển thị admin UI.
/// </summary>
public sealed class ProductUnitBarcodeReadService : IProductUnitBarcodeReadService
{
    private readonly IProductVariantUnitBarcodeRepository _barcodeRepository;
    private readonly ICurrentStore _currentStore;

    public ProductUnitBarcodeReadService(
        IProductVariantUnitBarcodeRepository barcodeRepository,
        ICurrentStore currentStore)
    {
        _barcodeRepository = barcodeRepository;
        _currentStore = currentStore;
    }

    public async Task<List<ProductVariantUnitBarcodeDto>> GetByConversionIdAsync(int conversionId, CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var items = await _barcodeRepository.GetByConversionIdAsync(storeId, conversionId, ct);

        return items.Select(x => new ProductVariantUnitBarcodeDto
        {
            Id = x.Id,
            ProductUnitConversionId = x.ProductUnitConversionId,
            Barcode = x.Barcode,
            BarcodeType = x.BarcodeType,
            IsPrimary = x.IsPrimary,
            IsActive = x.IsActive,
            Note = x.Note,
            CreatedAtUtc = x.CreatedAtUtc
        }).ToList();
    }
}