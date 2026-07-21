namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// DTO đọc nhanh số dư tồn kho hiện tại.
/// 
/// Công thức chuẩn:
/// AvailableQty = OnHandQty - ReservedQty
/// 
/// Lưu ý phase hiện tại:
/// - ReservedQty chưa được activate đầy đủ trong toàn bộ luồng nghiệp vụ.
/// - Vì vậy AvailableQty hiện thường sẽ gần bằng OnHandQty.
/// - DTO vẫn giữ sẵn cấu trúc này để tương thích Phase 6 (Reservation / Pick-Pack).
/// </summary>
public class InventoryBalanceDto
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public string ProductVariantName { get; set; } = null!;

    /// <summary>
    /// Tồn hiện có theo sổ hệ thống.
    /// </summary>
    public decimal OnHandQty { get; set; }

    /// <summary>
    /// Số lượng đang reserve / giữ chỗ.
    /// Phase hiện tại có thể chưa dùng đầy đủ.
    /// </summary>
    public decimal ReservedQty { get; set; }

    /// <summary>
    /// Số lượng khả dụng để bán / xuất.
    /// Công thức: OnHandQty - ReservedQty
    /// </summary>
    public decimal AvailableQty { get; set; }
}