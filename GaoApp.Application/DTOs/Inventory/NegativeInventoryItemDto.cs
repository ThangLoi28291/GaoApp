namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Dòng tồn kho hiện tại đang âm.
/// Dùng cho màn hình cảnh báo âm kho.
/// </summary>
public class NegativeInventoryItemDto
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public bool AllowNegativeInventory { get; set; }
    public int ProductVariantId { get; set; }
    public string ProductVariantName { get; set; } = string.Empty;

    public decimal OnHandQty { get; set; }
    public decimal ReservedQty { get; set; }
    public decimal AvailableQty { get; set; }
}