namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// 1 dòng hiển thị tồn kho hiện tại.
/// </summary>
public class InventoryBalanceListItemDto
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;

    public int ProductVariantId { get; set; }

    /// <summary>
    /// Tên hiển thị variant.
    /// Ví dụ: Bánh tráng cuốn | SKU...
    /// </summary>
    public string ProductVariantName { get; set; } = string.Empty;

    public string? Sku { get; set; }
    public string? Barcode { get; set; }

    public decimal OnHandQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal AvailableQty { get; set; }

    public bool IsNegative { get; set; }
}