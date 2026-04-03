namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Kết quả sau khi điều chỉnh kho.
/// </summary>
public class StockAdjustmentResultDto
{
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }

    public int? UnitId { get; set; }
    public string? UnitName { get; set; }

    /// <summary>
    /// Hệ số quy đổi về đơn vị gốc.
    /// </summary>
    public decimal Factor { get; set; }

    /// <summary>
    /// Số lượng user nhập theo đơn vị đang chọn.
    /// </summary>
    public decimal InputQuantity { get; set; }

    /// <summary>
    /// Số lượng quy đổi về đơn vị gốc.
    /// </summary>
    public decimal BaseQuantity { get; set; }

    public decimal BeforeQty { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal AfterQty { get; set; }

    public bool IsNegativeAfterAdjustment { get; set; }

    public string Message { get; set; } = string.Empty;
}