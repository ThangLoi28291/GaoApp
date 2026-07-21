namespace GaoApp.Application.DTOs.Inventory.Warehouse;

public class CreateWarehouseRequest
{
    /// <summary>
    /// Phase 22.1: null giữ tương thích UI cũ và tự dùng LegalEntity mặc định
    /// nhập hàng/ưu tiên đầu tiên. Phase 22.2 sẽ cho admin chọn rõ ràng.
    /// </summary>
    public int? LegalEntityId { get; set; }
    public string Name { get; set; } = null!;
    public string? Location { get; set; }
    public string? Note { get; set; }
    public bool IsDefault { get; set; }
    public bool AllowNegativeInventory { get; set; }
}
