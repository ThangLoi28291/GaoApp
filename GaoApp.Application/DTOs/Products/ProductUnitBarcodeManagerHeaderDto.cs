namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// Header thông tin để màn quản lý barcode hiển thị tên sản phẩm / SKU / đơn vị bán.
/// Tách riêng DTO này để Web không phải gọi repository trực tiếp.
/// </summary>
public sealed class ProductUnitBarcodeManagerHeaderDto
{
    public int ProductUnitConversionId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantSku { get; set; } = string.Empty;
    public string? UnitName { get; set; }
}