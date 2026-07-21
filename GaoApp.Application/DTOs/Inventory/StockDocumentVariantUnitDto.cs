namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// DTO trả danh sách đơn vị dùng cho màn chứng từ kho.
/// </summary>
public sealed class StockDocumentVariantUnitDto
{
    public int ProductUnitConversionId { get; set; }
    public int UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal Factor { get; set; }
    public bool IsBaseUnit { get; set; }
    public bool IsDefaultForSale { get; set; }
    public bool IsActive { get; set; }
}