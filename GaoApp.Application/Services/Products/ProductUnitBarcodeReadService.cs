using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Products;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Products;

namespace GaoApp.Application.Services.Products;

/// <summary>
/// Service đọc dữ liệu barcode theo đơn vị bán để hiển thị admin UI.
/// CHỐT:
/// - Web chỉ gọi service
/// - Service tự lấy StoreId hiện tại
/// - Web không gọi repository trực tiếp nữa
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

    public async Task<List<ProductVariantUnitBarcodeDto>> GetByConversionIdAsync(
        int conversionId,
        CancellationToken ct = default)
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

    public async Task<ProductUnitBarcodeManagerHeaderDto?> GetManagerHeaderAsync(
        int conversionId,
        CancellationToken ct = default)
    {
        var storeId = _currentStore.StoreId;

        var conversion = await _barcodeRepository.GetConversionDetailAsync(storeId, conversionId, ct);
        if (conversion == null)
            return null;

        return new ProductUnitBarcodeManagerHeaderDto
        {
            ProductUnitConversionId = conversion.Id,
            ProductName = conversion.ProductVariant.Product.Name,
            VariantSku = conversion.ProductVariant.Sku,
            UnitName = conversion.Unit?.Name
        };
    }
}