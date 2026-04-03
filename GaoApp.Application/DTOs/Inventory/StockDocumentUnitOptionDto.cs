namespace GaoApp.Application.DTOs.Inventory;

public class StockDocumentUnitOptionDto
{
    public int UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;

    /// <summary>
    /// Hệ số quy đổi về base unit
    /// Ví dụ: 1 thùng = 20 bịch => Factor = 20
    /// </summary>
    public decimal ConversionFactor { get; set; }

    /// <summary>
    /// Barcode của đơn vị này nếu có
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// Đánh dấu đơn vị mặc định khi nhập
    /// </summary>
    public bool IsDefault { get; set; }
}